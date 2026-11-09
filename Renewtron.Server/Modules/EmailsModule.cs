using System.Security.Claims;
using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// The admin's Emails page: every email Renewtron sent or tried to, with SendGrid's answer and what
/// happened at the inbox; resending one; a test email; and SendGrid's event webhook.
/// </summary>
public sealed class EmailsModule : ICarterModule
{
    public record TestEmailRequest(string? To);

    /// <summary>The inbox tabs. "Problem" = failed to send, or bounced/dropped/spam at the inbox.</summary>
    public static readonly string[] Tabs = ["all", "problem", "sent", "delivered", "opened"];

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/emails").RequireAuthorization().WithTags("Admin.Emails");

        group.MapGet("/", async (ApplicationDbContext db, string? tab = null, string? kind = null, string? search = null,
            Guid? clientId = null, int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var now = DateTime.UtcNow;

            IQueryable<OutboundMessage> Base()
            {
                var q = db.OutboundMessages.AsNoTracking().Where(m => m.Channel == OutboundChannel.Email);
                if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(m => m.Template == kind);
                if (clientId is { } c) q = q.Where(m => m.ClientId == c);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLower();
                    q = q.Where(m => m.Recipient.ToLower().Contains(term) || m.Subject.ToLower().Contains(term));
                }
                return q;
            }

            static IQueryable<OutboundMessage> InTab(IQueryable<OutboundMessage> q, string? t) => (t ?? "all") switch
            {
                "problem" => q.Where(m => m.Status == OutboundStatus.Failed
                    || m.DeliveryStatus == "bounce" || m.DeliveryStatus == "dropped" || m.DeliveryStatus == "spamreport"),
                "sent" => q.Where(m => m.Status == OutboundStatus.Sent && m.DeliveryStatus == null),
                "delivered" => q.Where(m => m.DeliveryStatus == "delivered"),
                "opened" => q.Where(m => m.OpenedAt != null),
                _ => q,
            };

            var filtered = InTab(Base(), tab);
            var total = await filtered.CountAsync();
            var items = await filtered.OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(m => new
                {
                    id = m.Id, kind = m.Template, subject = m.Subject, to = m.Recipient,
                    status = m.Status.ToString(), deliveryStatus = m.DeliveryStatus, openedAt = m.OpenedAt,
                    error = m.ErrorMessage, createdAt = m.CreatedAt, sentAt = m.SentAt, attemptCount = m.AttemptCount,
                    clientId = m.ClientId,
                })
                .ToListAsync();
            var counts = new Dictionary<string, int>();
            foreach (var t in Tabs) counts[t] = await InTab(Base(), t).CountAsync();
            var kinds = await db.OutboundMessages.AsNoTracking().Where(m => m.Channel == OutboundChannel.Email)
                .Select(m => m.Template).Distinct().OrderBy(k => k).ToListAsync();

            return Results.Ok(new { total, page, pageSize, counts, kinds, items, problem = await EmailEvents.ProblemAsync(db, now) });
        });

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var m = await db.OutboundMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (m is null) return Results.NotFound();
            var client = m.ClientId is { } cid
                ? await db.Clients.AsNoTracking().Where(c => c.Id == cid).Select(c => new { id = c.Id, fullName = c.FullName }).FirstOrDefaultAsync()
                : null;
            var resends = await db.OutboundMessages.AsNoTracking().Where(x => x.ResendOfId == id)
                .OrderBy(x => x.CreatedAt).Select(x => new { id = x.Id, status = x.Status.ToString(), createdAt = x.CreatedAt }).ToListAsync();
            return Results.Ok(new
            {
                id = m.Id, kind = m.Template, subject = m.Subject, to = m.Recipient,
                status = m.Status.ToString(), error = m.ErrorMessage, createdAt = m.CreatedAt, sentAt = m.SentAt,
                attemptCount = m.AttemptCount, lastAttemptAt = m.LastAttemptAt,
                providerMessageId = m.ProviderMessageId,
                deliveryStatus = m.DeliveryStatus, deliveryAt = m.DeliveryAt, deliveryDetail = m.DeliveryDetail, openedAt = m.OpenedAt,
                bodyHtml = m.BodyHtml, bodyText = m.BodyText,
                templateKey = m.TemplateKey, templateVersion = m.TemplateVersion,
                templateName = m.TemplateKey is null ? null : Renewtron.Services.Email.EmailTemplateCatalog.Find(m.TemplateKey)?.Name,
                leadId = m.LeadId, renewalRequestId = m.RenewalRequestId, batchId = m.BatchId, resendOfId = m.ResendOfId,
                client, resends,
            });
        });

        // Sends the stored email again to the same address, as a new message linked to this one.
        group.MapPost("/{id:guid}/resend", async (Guid id, ApplicationDbContext db, IEmailSender sender) =>
        {
            var m = await db.OutboundMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (m is null) return Results.NotFound();
            if (m.BodyHtml is null && m.BodyText is null)
                return Results.Conflict(new { error = "This email's text is no longer kept (older than 180 days), so it can't be resent." });
            try
            {
                var newId = await sender.SendAsync(new EmailToSend(m.Template, m.Recipient, m.Subject, m.BodyText ?? "", m.BodyHtml ?? m.BodyText ?? "",
                    LeadId: m.LeadId, ClientId: m.ClientId, RenewalRequestId: m.RenewalRequestId, ResendOfId: m.Id,
                    // The same email again: it keeps the template version it was written from.
                    TemplateKey: m.TemplateKey, TemplateVersion: m.TemplateVersion));
                return Results.Ok(new { id = newId, message = $"Sent again to {m.Recipient}." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.UnprocessableEntity(new { error = ex.Message });
            }
        });

        // Settings → SendGrid → Send test email: through the same sender, so it shows up on the Emails page.
        app.MapPost("/api/admin/settings/sendgrid/test-email", async (TestEmailRequest? body, IEmailSender sender, ClaimsPrincipal user) =>
        {
            var to = string.IsNullOrWhiteSpace(body?.To) ? user.Identity?.Name : body!.To!.Trim();
            if (string.IsNullOrWhiteSpace(to) || !to.Contains('@')) return Results.BadRequest(new { error = "No address to send it to." });
            try
            {
                await sender.SendAsync(new EmailToSend("test", to, "Renewtron test email",
                    "This is a test email from Renewtron. If you can read it, SendGrid is sending our emails.",
                    "<p>This is a test email from Renewtron.</p><p>If you can read it, SendGrid is sending our emails.</p>"));
                return Results.Ok(new { ok = true, message = $"SendGrid took it; it's on its way to {to}." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Ok(new { ok = false, message = ex.Message });
            }
        }).RequireAuthorization().WithTags("Admin.Settings");

        // SendGrid → Settings → Mail Settings → Event Webhook: {site}/api/sendgrid/events, signed
        // (its verification key goes in Settings → SendGrid). Events update the matching email.
        app.MapPost("/api/sendgrid/events", async (HttpRequest request, ApplicationDbContext db,
            IOptionsSnapshot<SendGridSettings> settings, ILogger<EmailsModule> logger) =>
        {
            var key = settings.Value.EventWebhookPublicKey;
            if (string.IsNullOrWhiteSpace(key)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var body = await new StreamReader(request.Body).ReadToEndAsync();
            var signature = request.Headers["X-Twilio-Email-Event-Webhook-Signature"].ToString();
            var timestamp = request.Headers["X-Twilio-Email-Event-Webhook-Timestamp"].ToString();
            if (signature.Length == 0 || timestamp.Length == 0 || !EmailEvents.VerifySignature(key, signature, timestamp, body))
            {
                logger.LogWarning("Rejected a SendGrid event webhook call: bad or missing signature");
                return Results.Unauthorized();
            }

            IReadOnlyList<EmailEvents.Event> events;
            try { events = EmailEvents.Parse(body); }
            catch (System.Text.Json.JsonException) { return Results.BadRequest(); }

            var outboundIds = events.Where(e => e.OutboundId != null).Select(e => e.OutboundId!.Value).Distinct().ToList();
            var messageIds = events.Where(e => e.MessageId != null).Select(e => e.MessageId!).Distinct().ToList();
            var rows = await db.OutboundMessages
                .Where(m => outboundIds.Contains(m.Id) || (m.ProviderMessageId != null && messageIds.Contains(m.ProviderMessageId)))
                .ToListAsync();
            var byId = rows.ToDictionary(m => m.Id);
            var byMessage = rows.Where(m => m.ProviderMessageId != null).GroupBy(m => m.ProviderMessageId!).ToDictionary(g => g.Key, g => g.First());

            var applied = 0;
            foreach (var e in events.OrderBy(e => e.At))
            {
                var row = e.OutboundId is { } oid && byId.TryGetValue(oid, out var r1) ? r1
                    : e.MessageId is { } mid && byMessage.TryGetValue(mid, out var r2) ? r2 : null;
                if (row is null) continue; // not one of ours (or from before the log)
                EmailEvents.Apply(row, e);
                applied++;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { received = events.Count, applied });
        }).WithTags("Webhooks");
    }
}
