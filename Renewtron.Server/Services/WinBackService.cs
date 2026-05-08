using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;

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
    private readonly IOptionsMonitor<WinBackSettings> _winBack;
    private readonly ILogger<WinBackService> _logger;

    public WinBackService(
        ApplicationDbContext db,
        ILeadEmailService leadEmail,
        IOptionsMonitor<WinBackSettings> winBack,
        ILogger<WinBackService> logger)
    {
        _db = db;
        _leadEmail = leadEmail;
        _winBack = winBack;
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

        var template = _winBack.CurrentValue;
        return new WinBackPreview(
            RecipientCount: leads.Count,
            RecoverableValue: leads.Count * Math.Max(avgBasket, 0m),
            SampleNames: leads.Take(3).Select(l => l.FullName ?? "—").ToArray(),
            Subject: template.Subject,
            BodyPreview: template.BodyPlain);
    }

    public async Task<WinBackSendResult> SendAsync(WinBackFilter filter)
    {
        var batchId = Guid.NewGuid();
        var query = BuildEligibleQuery(filter);

        // Pull leads + their first business name (for {{BusinessName}}) in one go.
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

        var template = _winBack.CurrentValue;
        var now = DateTime.UtcNow;

        // Insert OutboundMessage rows up front (status=Queued); send happens immediately
        // afterwards in the same request. Failures are recorded individually.
        var outbound = leads.Select(l => new OutboundMessage
        {
            Id = Guid.NewGuid(),
            LeadId = l.Id,
            Channel = OutboundChannel.Email,
            Template = "win-back",
            Subject = template.Subject,
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
                await _leadEmail.SendWinBackEmailAsync(lead, leads[i].FirstBusinessName);
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
            await _leadEmail.SendWinBackEmailAsync(msg.Lead, firstBusinessName);
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
