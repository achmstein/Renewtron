using System.Security.Claims;
using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Services.Email;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// Emails → Templates: the wording of every customer email, and the layout they share. Read one, preview a
/// draft (with sample values or a real customer's), save it (refused while it has problems), reset it to
/// the wording in code, switch it off, send a test, and its versions: compare and restore.
/// </summary>
public sealed class EmailTemplatesModule : ICarterModule
{
    public record ContentBody(string? Subject, string? Heading, string? Body, string? Note);
    /// <summary>A draft to preview or test, and whose values to fill it with (no SampleId: the sample values).</summary>
    public record PreviewBody(string? Subject, string? Heading, string? Body, string? Layout, Guid? SampleId, string? To);
    public record EnabledBody(bool Enabled);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/email-templates").RequireAuthorization().WithTags("Admin.EmailTemplates");

        group.MapGet("/", async (ApplicationDbContext db) =>
        {
            var rows = await db.EmailTemplates.AsNoTracking().ToDictionaryAsync(t => t.Key);
            var latest = await db.EmailTemplateVersions.AsNoTracking()
                .GroupBy(v => v.TemplateKey)
                .Select(g => new { key = g.Key, version = g.Max(v => v.Version) })
                .ToDictionaryAsync(x => x.key, x => x.version);
            var lastSent = await db.OutboundMessages.AsNoTracking()
                .Where(m => m.Channel == OutboundChannel.Email)
                .GroupBy(m => m.Template)
                .Select(g => new { kind = g.Key, at = g.Max(m => m.CreatedAt) })
                .ToDictionaryAsync(x => x.kind, x => (DateTime?)x.at);

            object Row(string key, string name, string description) => new
            {
                key, name, description,
                enabled = !rows.TryGetValue(key, out var r) || r.Enabled,
                version = latest.GetValueOrDefault(key),
                updatedAt = rows.GetValueOrDefault(key)?.UpdatedAt,
                updatedBy = rows.GetValueOrDefault(key)?.UpdatedBy,
                lastSentAt = lastSent.GetValueOrDefault(key),
            };

            return Results.Ok(new
            {
                templates = EmailTemplateCatalog.All.Select(d => Row(d.Key, d.Name, d.Description)),
                layout = Row(EmailTemplateCatalog.Layout, "Layout", "The header, footer and styles every email shares."),
            });
        });

        group.MapGet("/{key}", async (string key, IEmailTemplates templates) =>
        {
            if (!Known(key)) return Results.NotFound();
            var isLayout = key == EmailTemplateCatalog.Layout;
            var d = EmailTemplateCatalog.Find(key);
            var current = await templates.CurrentAsync(key);
            return Results.Ok(new
            {
                key,
                name = d?.Name ?? "Layout",
                description = d?.Description ?? "The header, footer and styles every email shares. Each email's message goes where {{ content }} is.",
                isLayout,
                previewSource = d?.PreviewSource,
                enabled = await templates.IsEnabledAsync(key),
                variables = (isLayout ? EmailLayout.Variables : d!.Variables).Select(v => new
                {
                    name = v.Name, label = v.Label, description = v.Description, kind = v.Kind.ToString().ToLowerInvariant(),
                    sample = v.Sample,
                }),
                current = Content(current),
                @default = Content(templates.DefaultFor(key)),
            });
        });

        group.MapPost("/{key}/preview", async (string key, PreviewBody body, IEmailTemplates templates, ApplicationDbContext db,
            IOptionsSnapshot<PortalSettings> portal) =>
        {
            if (!Known(key)) return Results.NotFound();
            var draft = await DraftAsync(key, body, templates);
            var model = await ModelAsync(key, body.SampleId, db, portal.Value);
            if (model is null) return Results.NotFound(new { error = "That record is gone." });
            var problems = await ProblemsAsync(key, draft.Content, draft.Layout);
            var rendered = await EmailTemplateRenderer.RenderAsync(draft.Content, draft.Layout, model);
            return Results.Ok(new
            {
                subject = rendered.Subject, html = rendered.Html, text = rendered.Text,
                problems = problems.Concat(rendered.Problems).Distinct().Select(p => new { field = p.Field, message = p.Message }),
            });
        });

        group.MapPut("/{key}", async (string key, ContentBody body, IEmailTemplates templates, ClaimsPrincipal user) =>
        {
            if (!Known(key)) return Results.NotFound();
            var isLayout = key == EmailTemplateCatalog.Layout;
            var content = new TemplateContent(isLayout ? "" : body.Subject ?? "", isLayout ? "" : body.Heading ?? "", body.Body ?? "");
            if (!isLayout && string.IsNullOrWhiteSpace(content.Subject))
                return Results.BadRequest(new { error = "The email needs a subject.", problems = new[] { new { field = "subject", message = "The email needs a subject." } } });
            var problems = await ProblemsAsync(key, content, isLayout ? content.Body : await templates.LayoutAsync());
            if (problems.Count > 0)
                return Results.BadRequest(new { error = "Fix these before saving.", problems = problems.Select(p => new { field = p.Field, message = p.Message }) });

            var current = await templates.CurrentAsync(key);
            if (current.Subject == content.Subject && current.Heading == content.Heading && current.Body == content.Body)
                return Results.Ok(new { version = current.Version, message = "No changes to save." });
            var version = await templates.SaveAsync(key, content, user.Identity?.Name, body.Note);
            return Results.Ok(new { version, message = $"Saved as version {version}. It's used from the next email on." });
        });

        group.MapPost("/{key}/reset", async (string key, IEmailTemplates templates, ClaimsPrincipal user) =>
        {
            if (!Known(key)) return Results.NotFound();
            var current = await templates.CurrentAsync(key);
            var d = templates.DefaultFor(key);
            if (current.Version == 0 || (current.Subject == d.Subject && current.Heading == d.Heading && current.Body == d.Body))
                return Results.Ok(new { version = current.Version, message = "It's already the default." });
            var version = await templates.SaveAsync(key, d, user.Identity?.Name, "Reset to default", EmailTemplateVersionSource.Default);
            return Results.Ok(new { version, message = $"Back to the default wording (saved as version {version})." });
        });

        group.MapPut("/{key}/enabled", async (string key, EnabledBody body, IEmailTemplates templates, ClaimsPrincipal user) =>
        {
            if (!Known(key) || key == EmailTemplateCatalog.Layout) return Results.NotFound();
            await templates.SetEnabledAsync(key, body.Enabled, user.Identity?.Name);
            return Results.Ok(new { enabled = body.Enabled, message = body.Enabled ? "Switched on: it sends again." : "Switched off: Renewtron won't send it." });
        });

        // Sends the draft (as previewed) to the admin, marked [Test]; it shows on the Emails page as "template test".
        group.MapPost("/{key}/test", async (string key, PreviewBody body, IEmailTemplates templates, ApplicationDbContext db,
            IOptionsSnapshot<PortalSettings> portal, IEmailSender sender, ClaimsPrincipal user) =>
        {
            if (!Known(key)) return Results.NotFound();
            var to = string.IsNullOrWhiteSpace(body.To) ? user.Identity?.Name : body.To.Trim();
            if (string.IsNullOrWhiteSpace(to) || !to.Contains('@')) return Results.BadRequest(new { error = "No address to send it to." });
            var draft = await DraftAsync(key, body, templates);
            var model = await ModelAsync(key, body.SampleId, db, portal.Value);
            if (model is null) return Results.NotFound(new { error = "That record is gone." });
            var rendered = await EmailTemplateRenderer.RenderAsync(draft.Content, draft.Layout, model);
            if (!rendered.Ok) return Results.BadRequest(new { error = rendered.Problems[0].Message });
            try
            {
                await sender.SendAsync(new EmailToSend("template test", to, "[Test] " + rendered.Subject, rendered.Text, rendered.Html));
                return Results.Ok(new { message = $"Sent to {to}." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.UnprocessableEntity(new { error = ex.Message });
            }
        });

        // Real records to preview with: renewals for renewal emails, leads for the rest.
        group.MapGet("/{key}/samples", async (string key, ApplicationDbContext db, string? search = null) =>
        {
            if (!Known(key)) return Results.NotFound();
            var term = search?.Trim().ToLower();
            if (Source(key) == "renewal")
            {
                var q = db.RenewalRequests.AsNoTracking().Where(r => !r.IsTest && r.Email != null);
                if (!string.IsNullOrEmpty(term))
                    q = q.Where(r => r.Email!.ToLower().Contains(term) || r.SearchResult.BusinessName.ToLower().Contains(term)
                                     || r.SearchResult.SearchLog.Abn.Contains(term));
                var rows = await q.OrderByDescending(r => r.InitiatedAt).Take(20)
                    .Select(r => new { id = r.Id, title = r.SearchResult.BusinessName, detail = r.Email, at = r.InitiatedAt })
                    .ToListAsync();
                return Results.Ok(rows);
            }
            var leads = db.Leads.AsNoTracking().Where(l => l.Email != "");
            if (!string.IsNullOrEmpty(term))
                leads = leads.Where(l => l.Email.ToLower().Contains(term) || l.FullName.ToLower().Contains(term) || l.Abn.Contains(term));
            return Results.Ok(await leads.OrderByDescending(l => l.CreatedAt).Take(20)
                .Select(l => new { id = l.Id, title = l.FullName, detail = l.Email, at = l.CreatedAt })
                .ToListAsync());
        });

        // ── Versions ──

        group.MapGet("/{key}/versions", async (string key, ApplicationDbContext db) =>
        {
            if (!Known(key)) return Results.NotFound();
            var versions = await db.EmailTemplateVersions.AsNoTracking().Where(v => v.TemplateKey == key)
                .OrderByDescending(v => v.Version)
                .Select(v => new
                {
                    version = v.Version, source = v.Source.ToString().ToLowerInvariant(), restoredFrom = v.RestoredFrom,
                    note = v.Note, createdAt = v.CreatedAt, createdBy = v.CreatedBy,
                    sent = db.OutboundMessages.Count(m => m.TemplateKey == key && m.TemplateVersion == v.Version),
                })
                .ToListAsync();
            var sentWithDefault = await db.OutboundMessages.CountAsync(m => m.TemplateKey == key && m.TemplateVersion == 0);
            return Results.Ok(new { versions, sentWithDefault });
        });

        // Version 0 is the wording in code.
        group.MapGet("/{key}/versions/{version:int}", async (string key, int version, ApplicationDbContext db, IEmailTemplates templates) =>
        {
            if (!Known(key)) return Results.NotFound();
            if (version == 0) return Results.Ok(Content(templates.DefaultFor(key)));
            var v = await db.EmailTemplateVersions.AsNoTracking().FirstOrDefaultAsync(x => x.TemplateKey == key && x.Version == version);
            return v is null ? Results.NotFound() : Results.Ok(Content(new TemplateContent(v.Subject, v.Heading, v.Body, v.Version)));
        });

        group.MapPost("/{key}/versions/{version:int}/restore", async (string key, int version, ApplicationDbContext db,
            IEmailTemplates templates, ClaimsPrincipal user) =>
        {
            if (!Known(key)) return Results.NotFound();
            TemplateContent? old = version == 0
                ? templates.DefaultFor(key)
                : await db.EmailTemplateVersions.AsNoTracking().Where(x => x.TemplateKey == key && x.Version == version)
                    .Select(x => new TemplateContent(x.Subject, x.Heading, x.Body, x.Version)).FirstOrDefaultAsync();
            if (old is null) return Results.NotFound();

            // Still has to pass today's checks (a variable may have been removed since).
            var problems = await ProblemsAsync(key, old, key == EmailTemplateCatalog.Layout ? old.Body : await templates.LayoutAsync());
            if (problems.Count > 0)
                return Results.BadRequest(new { error = $"Version {version} can't be restored as it is: {problems[0].Message}", problems });

            var what = version == 0 ? "The default wording" : $"Version {version}";
            var saved = await templates.SaveAsync(key, old, user.Identity?.Name, $"Restored {what.ToLowerInvariant()}",
                version == 0 ? EmailTemplateVersionSource.Default : EmailTemplateVersionSource.Rollback, version);
            return Results.Ok(new { version = saved, message = $"{what} is back (saved as version {saved})." });
        });
    }

    private static bool Known(string key) => key == EmailTemplateCatalog.Layout || EmailTemplateCatalog.Find(key) is not null;

    private static string Source(string key) => EmailTemplateCatalog.Find(key)?.PreviewSource ?? "renewal";

    private static object Content(TemplateContent c) => new { subject = c.Subject, heading = c.Heading, body = c.Body, version = c.Version };

    /// <summary>The draft to render. For the layout, a draft layout around renewal confirmation's current wording.</summary>
    private static async Task<(TemplateContent Content, string Layout)> DraftAsync(string key, PreviewBody body, IEmailTemplates templates)
    {
        if (key == EmailTemplateCatalog.Layout)
            return (await templates.CurrentAsync(EmailTemplateCatalog.RenewalConfirmation), body.Body ?? body.Layout ?? "");
        return (new TemplateContent(body.Subject ?? "", body.Heading ?? "", body.Body ?? ""), body.Layout ?? await templates.LayoutAsync());
    }

    private static async Task<IReadOnlyList<TemplateProblem>> ProblemsAsync(string key, TemplateContent content, string layout) =>
        key == EmailTemplateCatalog.Layout
            ? await EmailTemplateRenderer.CheckLayoutAsync(layout)
            : await EmailTemplateRenderer.CheckAsync(EmailTemplateCatalog.Find(key)!, content, layout);

    /// <summary>The values to fill a preview with: the sample values, or a real renewal's or lead's. Null when the record is gone.</summary>
    private static async Task<Dictionary<string, object?>?> ModelAsync(string key, Guid? sampleId, ApplicationDbContext db, PortalSettings portal)
    {
        var d = EmailTemplateCatalog.Find(key) ?? EmailTemplateCatalog.Find(EmailTemplateCatalog.RenewalConfirmation)!;
        if (sampleId is not { } id) return EmailTemplateCatalog.SampleModel(d);

        if (d.PreviewSource == "renewal")
        {
            var r = await db.RenewalRequests.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Email, x.RenewalYears, x.Amount, x.TransactionReference, x.CompletedAt, x.InitiatedAt, x.LeadId,
                    Name = x.SearchResult.BusinessName, Abn = x.SearchResult.SearchLog.Abn,
                })
                .FirstOrDefaultAsync();
            if (r is null) return null;
            if (d.Key == EmailTemplateCatalog.PaidEarly)
            {
                // Everything they paid for in the same checkout.
                var checkout = r.LeadId is { } lid
                    ? await db.RenewalRequests.AsNoTracking()
                        .Where(x => x.LeadId == lid && x.InitiatedAt >= r.InitiatedAt.AddMinutes(-5) && x.InitiatedAt <= r.InitiatedAt.AddMinutes(5))
                        .Select(x => new { x.SearchResult.BusinessName, x.Amount }).ToListAsync()
                    : [];
                if (checkout.Count == 0) checkout = [new { BusinessName = r.Name, r.Amount }];
                return EmailModels.PaidEarly(r.Email!, r.Abn, checkout.Select(x => x.BusinessName).Distinct().ToArray(),
                    r.RenewalYears, checkout.Sum(x => x.Amount));
            }
            return EmailModels.RenewalConfirmation(portal, r.Email!, r.Name, r.Abn, r.RenewalYears, r.Amount,
                r.TransactionReference ?? "N/A", r.CompletedAt ?? r.InitiatedAt);
        }

        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (lead is null) return null;
        var first = await db.SearchResults.AsNoTracking().Where(s => s.SearchLogId == lead.SearchLogId)
            .OrderBy(s => s.Id).Select(s => s.BusinessName).FirstOrDefaultAsync();
        return EmailModels.Lead(lead, first);
    }
}
