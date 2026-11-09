using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services.Email;

namespace Renewtron.Services;

public interface IWinBackService
{
    Task<WinBackPreview> PreviewAsync(WinBackFilter filter, decimal avgBasket);
    Task<WinBackSendResult> SendAsync(WinBackFilter filter);
    Task SendOneAsync(Guid outboundMessageId);
}

/// <summary>Filter narrowing the recipient set. Mirrors the admin Leads list filter.</summary>
public class WinBackFilter
{
    public string? Outcome { get; set; }
    public bool? ReminderOptIn { get; set; }
    public string? Search { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public record WinBackPreview(
    int RecipientCount,
    decimal RecoverableValue,
    string[] SampleNames,
    string Subject,
    string BodyPreview);

public record WinBackSendResult(int Enqueued, Guid BatchId);

/// <summary>
/// Builds the recipient list for a "win-back" email blast and dispatches them via the
/// existing SendGrid-based ILeadEmailService. Each send is recorded in OutboundMessages.
///
/// Eligibility (default): Lead.Outcome = RenewalAvailable AND ConvertedToRenewal = false.
/// Filters are additive on top.
/// </summary>
public class WinBackService : IWinBackService
{
    private readonly ApplicationDbContext _db;
    private readonly ILeadEmailService _leadEmail;
    private readonly IEmailTemplates _templates;
    private readonly ILogger<WinBackService> _logger;

    public WinBackService(
        ApplicationDbContext db,
        ILeadEmailService leadEmail,
        IEmailTemplates templates,
        ILogger<WinBackService> logger)
    {
        _db = db;
        _leadEmail = leadEmail;
        _templates = templates;
        _logger = logger;
    }

    public async Task<WinBackPreview> PreviewAsync(WinBackFilter filter, decimal avgBasket)
    {
        var query = BuildEligibleQuery(filter);
        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(500)
            .Select(l => new { l.Id, l.FullName, l.Abn })
            .ToListAsync();

        // The first recipient's email as they'd get it (sample values when there's nobody yet).
        var firstLead = leads.Count > 0 ? await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leads[0].Id) : null;
        var model = firstLead is null
            ? EmailTemplateCatalog.SampleModel(EmailTemplateCatalog.Find(EmailTemplateCatalog.WinBack)!)
            : EmailModels.Lead(firstLead);
        var email = await _templates.RenderAsync(EmailTemplateCatalog.WinBack, model);
        return new WinBackPreview(
            RecipientCount: leads.Count,
            RecoverableValue: leads.Count * Math.Max(avgBasket, 0m),
            SampleNames: leads.Take(3).Select(l => l.FullName ?? "—").ToArray(),
            Subject: email.Enabled ? email.Subject : "(The win-back email is switched off in Emails → Templates)",
            BodyPreview: email.Text);
    }

    public async Task<WinBackSendResult> SendAsync(WinBackFilter filter)
    {
        var batchId = Guid.NewGuid();
        var query = BuildEligibleQuery(filter);

        // Pull leads + their first business name (for {{ business_name }}) in one go.
        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(500) // safety cap per send
            .Select(l => new
            {
                l.Id,
                l.Email,
                FirstBusinessName = l.SearchLog == null
                    ? null
                    : l.SearchLog.Results.OrderBy(r => r.Id).Select(r => r.BusinessName).FirstOrDefault(),
            })
            .ToListAsync();

        if (leads.Count == 0)
            return new WinBackSendResult(0, batchId);

        if (!await _templates.IsEnabledAsync(EmailTemplateCatalog.WinBack))
            throw new InvalidOperationException("The win-back email is switched off in Emails → Templates.");
        var now = DateTime.UtcNow;

        // Insert OutboundMessage rows up front (status=Queued); send happens immediately
        // afterwards in the same request. Failures are recorded individually.
        var outbound = leads.Select(l => new OutboundMessage
        {
            Id = Guid.NewGuid(),
            LeadId = l.Id,
            Channel = OutboundChannel.Email,
            Template = "win-back",
            Subject = "", // filled in with each recipient's subject when it sends
            Recipient = l.Email ?? string.Empty,
            Status = OutboundStatus.Queued,
            CreatedAt = now,
            BatchId = batchId,
        }).ToList();

        _db.OutboundMessages.AddRange(outbound);
        await _db.SaveChangesAsync();

        // Send synchronously (small batches, simple flow). Each send is independent.
        for (var i = 0; i < leads.Count; i++)
        {
            var msg = outbound[i];
            try
            {
                var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == leads[i].Id);
                if (lead is null) continue;
                await _leadEmail.SendWinBackEmailAsync(lead, leads[i].FirstBusinessName, msg.Id);
                msg.Status = OutboundStatus.Sent;
                msg.SentAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                msg.Status = OutboundStatus.Failed;
                msg.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "Win-back send failed for lead {LeadId}", leads[i].Id);
            }
        }

        await _db.SaveChangesAsync();
        return new WinBackSendResult(leads.Count, batchId);
    }

    /// <summary>Send a single previously-queued OutboundMessage. Used by Hangfire if we move to async.</summary>
    public async Task SendOneAsync(Guid outboundMessageId)
    {
        var msg = await _db.OutboundMessages
            .Include(m => m.Lead)
            .FirstOrDefaultAsync(m => m.Id == outboundMessageId);
        if (msg is null || msg.Lead is null || msg.Status != OutboundStatus.Queued) return;

        var firstBusinessName = await _db.SearchResults
            .Where(r => r.SearchLog!.Id == msg.Lead.SearchLogId)
            .OrderBy(r => r.Id)
            .Select(r => r.BusinessName)
            .FirstOrDefaultAsync();

        try
        {
            await _leadEmail.SendWinBackEmailAsync(msg.Lead, firstBusinessName, msg.Id);
            msg.Status = OutboundStatus.Sent;
            msg.SentAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            msg.Status = OutboundStatus.Failed;
            msg.ErrorMessage = ex.Message;
        }
        await _db.SaveChangesAsync();
    }

    private IQueryable<Lead> BuildEligibleQuery(WinBackFilter filter)
    {
        var q = _db.Leads.AsNoTracking()
            .Where(l => l.Outcome == LeadOutcome.RenewalAvailable
                        && !l.ConvertedToRenewal
                        && !string.IsNullOrEmpty(l.Email));

        if (!string.IsNullOrEmpty(filter.Outcome) && Enum.TryParse<LeadOutcome>(filter.Outcome, out var oc))
            q = q.Where(l => l.Outcome == oc);
        if (filter.ReminderOptIn is { } r)
            q = q.Where(l => l.ReminderOptIn == r);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.ToLower();
            q = q.Where(l =>
                l.Abn.Contains(term) ||
                l.FullName.ToLower().Contains(term) ||
                l.Email.ToLower().Contains(term));
        }
        if (filter.CreatedFrom.HasValue)
            q = q.Where(l => l.CreatedAt >= filter.CreatedFrom.Value.ToUniversalTime());
        if (filter.CreatedTo.HasValue)
        {
            var until = filter.CreatedTo.Value.Date.AddDays(1).ToUniversalTime();
            q = q.Where(l => l.CreatedAt < until);
        }

        return q;
    }
}
