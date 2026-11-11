using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public interface IPaymentFollowUpService
{
    /// <summary>Emails, once each, visitors who opened the payment page and left without paying.</summary>
    Task<int> SendDueAsync(CancellationToken ct);
}

/// <summary>
/// "You didn't finish paying": goes a couple of hours after a visitor reached the payment page
/// and left (David, Nov 2026: yes, like the win-back emails). Only once per visit, never to a test
/// customer, someone who unsubscribed, or someone who has since paid on another visit.
/// </summary>
public class PaymentFollowUpService(
    ApplicationDbContext db,
    ILeadEmailService email,
    IOptionsMonitor<WinBackSettings> settings,
    ILogger<PaymentFollowUpService> logger) : IPaymentFollowUpService
{
    public const string RecurringJobId = "payment-follow-up";

    /// <summary>Older visits are left alone: a follow-up a week later reads as spam.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        var s = settings.CurrentValue;
        if (!s.PaymentFollowUpEnabled) return 0;
        var now = DateTime.UtcNow;
        var leftBefore = now - TimeSpan.FromHours(Math.Max(1, s.PaymentFollowUpAfterHours));
        var notBefore = now - MaxAge;

        var due = await Due(db, leftBefore, notBefore)
            .OrderBy(l => l.ReachedPaymentAt)
            .Take(100)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var lead in due)
        {
            var firstName = lead.SearchLogId == null ? null : await db.SearchResults.AsNoTracking()
                .Where(r => r.SearchLogId == lead.SearchLogId && r.IsAvailable).OrderBy(r => r.Id)
                .Select(r => r.BusinessName).FirstOrDefaultAsync(ct);
            // Stamped first: a send that throws half-way must not go again every hour.
            lead.PaymentFollowUpSentAt = now;
            await db.SaveChangesAsync(ct);
            try
            {
                if (await email.SendPaymentNotFinishedEmailAsync(lead, firstName, s.SiteUrl)) sent++;
                else
                {
                    // Switched off in Emails → Templates: leave the rest for when it's switched on.
                    lead.PaymentFollowUpSentAt = null;
                    await db.SaveChangesAsync(ct);
                    break;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Payment follow-up email failed for lead {LeadId}", lead.Id);
            }
        }
        if (sent > 0) logger.LogInformation("Payment follow-up: {Sent} emails sent", sent);
        return sent;
    }

    /// <summary>Who is due one: reached the payment page in the window, didn't pay on this visit or since.</summary>
    public static IQueryable<Lead> Due(ApplicationDbContext db, DateTime leftBefore, DateTime notBefore) => db.Leads
        .Where(l => l.Progress == LeadProgress.PaymentPage && !l.ConvertedToRenewal && !l.IsTest
            && l.PaymentFollowUpSentAt == null && l.Email != ""
            && l.ReachedPaymentAt != null && l.ReachedPaymentAt <= leftBefore && l.ReachedPaymentAt >= notBefore)
        // Paid on another visit since (same email or ABN).
        .Where(l => !db.Leads.Any(o => o.ConvertedToRenewal && o.ConvertedAt >= l.CreatedAt && (o.Email == l.Email || o.Abn == l.Abn)))
        // Asked us to stop emailing.
        .Where(l => l.ClientId == null || !db.Clients.Any(c => c.Id == l.ClientId && c.UnsubscribedAt != null));
}
