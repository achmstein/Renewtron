using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services.Registration;
using Renewtron.Settings;

namespace Renewtron.Services;

public interface IBpayAutoPayService
{
    /// <summary>Pays, through Airwallex, every ASIC BPAY bill that is due and safe to pay without a person.</summary>
    Task<BpayAutoPayReport> PayDueAsync(CancellationToken ct);
}

public sealed record BpayAutoPayReport(int Renewals, int Registrations, decimal Sent, string? StoppedBecause);

/// <summary>
/// Pays ASIC's BPAY bills on its own: renewals lodged to pay later and new-name applications. Before
/// this, a person picked them on "BPAY to pay" and confirmed the total. The guards that confirmation
/// gave are kept here: ASIC's set fee only, a daily cap, a few tries per bill, never test records,
/// and the same send-once protection as the manual button (it calls the same code).
/// </summary>
public class BpayAutoPayService(
    ApplicationDbContext db,
    IBpayPaymentService renewals,
    IBusinessNameRegistrationService registrations,
    IOptionsMonitor<AirwallexSettings> airwallex,
    IOptionsMonitor<AsicSettings> asic,
    ILogger<BpayAutoPayService> logger) : IBpayAutoPayService
{
    public const string RecurringJobId = "airwallex-bpay-autopay";

    /// <summary>After this many tries a bill waits for a person (a wrong reference won't fix itself).</summary>
    public const int MaxTries = 3;

    public async Task<BpayAutoPayReport> PayDueAsync(CancellationToken ct)
    {
        var settings = airwallex.CurrentValue;
        if (!settings.AutoPayBpay || !settings.Ready) return new(0, 0, 0, settings.Ready ? "Switched off" : "Airwallex isn't set up");
        // Sandbox transfers come back PAID without money moving: real ASIC bills would be marked
        // paid and never actually paid. Only a person may pay in sandbox, deliberately.
        if (settings.UseSandbox) return new(0, 0, 0, "Airwallex is in sandbox");

        var budget = settings.AutoPayDailyLimit - await SpentTodayAsync(ct);
        int paidRenewals = 0, paidRegistrations = 0;
        decimal sent = 0;
        string? stopped = null;

        // Renewals: bills lodged by a renewal run, unpaid, nothing under way.
        var bills = (await db.RenewalRequests.Include(r => r.SearchResult)
                .Where(r => !r.IsTest && r.BpayReference != null && r.BpayReference != "" && r.BpayBillerCode != null && r.BpayBillerCode != ""
                    && r.BpayPaidAt == null && r.BpayPaymentAttempts < MaxTries)
                .OrderBy(r => r.CompletedAt ?? r.InitiatedAt)
                .ToListAsync(ct))
            .Where(BpayPaymentService.IsPayable)
            .ToList();
        foreach (var bill in bills)
        {
            if (asic.CurrentValue.FeeFor(bill.RenewalYears) is not { } fee) { stopped = $"ASIC's {bill.RenewalYears}-year fee isn't set"; continue; }
            if (fee > budget) { stopped = "Daily limit reached"; break; }
            var (outcomes, refused) = await renewals.PayAsync([bill.Id], fee);
            if (refused is not null) { logger.LogWarning("BPAY auto-pay skipped renewal {Id}: {Why}", bill.Id, refused); continue; }
            if (outcomes.FirstOrDefault() is { Sent: true }) { paidRenewals++; sent += fee; budget -= fee; }
        }

        // New business names lodged to pay ASIC by BPAY.
        if (stopped != "Daily limit reached")
        {
            var regs = await db.BusinessNameRegistrations
                .Where(r => !r.IsTest && r.Status == BusinessNameRegistrationStatus.AwaitingAsicPayment && r.AsicPaidAt == null
                    && r.BpayBillerCode != null && r.BpayBillerCode != "" && r.BpayReference != null && r.BpayReference != ""
                    && r.BpayPaymentAttempts < MaxTries)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);
            foreach (var reg in regs.Where(r => r.AirwallexTransferStatus is null
                         || BpayPaymentService.Ended.Contains(r.AirwallexTransferStatus) || r.AirwallexTransferStatus == BpayPaymentService.Unknown))
            {
                if (asic.CurrentValue.FeeFor(reg.Years) is not { } fee) { stopped = $"ASIC's {reg.Years}-year fee isn't set"; continue; }
                if (fee > budget) { stopped = "Daily limit reached"; break; }
                var error = await registrations.PayAsicByBpayAsync(reg.Id, fee);
                if (error is null) { paidRegistrations++; sent += fee; budget -= fee; }
                else logger.LogWarning("BPAY auto-pay for registration {Id}: {Error}", reg.Id, error);
            }
        }

        if (paidRenewals + paidRegistrations > 0 || stopped is not null)
            logger.LogInformation("BPAY auto-pay: {Renewals} renewals, {Registrations} new names, A${Sent} sent{Stopped}",
                paidRenewals, paidRegistrations, sent, stopped is null ? "" : $"; stopped: {stopped}");
        return new(paidRenewals, paidRegistrations, sent, stopped);
    }

    /// <summary>
    /// What has gone out today (AEST) or is still on its way: paid today, or a transfer not yet
    /// finished. Counts manual payments too, so the cap covers everything.
    /// </summary>
    private async Task<decimal> SpentTodayAsync(CancellationToken ct)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
        var localMidnight = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        var since = TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz);
        var ended = BpayPaymentService.Ended.ToList();

        var renewalsSpent = await db.RenewalRequests
            .Where(r => r.BpayAmount != null && r.AirwallexTransferStatus != null
                && (r.BpayPaidAt >= since || (r.BpayPaidAt == null && !ended.Contains(r.AirwallexTransferStatus))))
            .SumAsync(r => r.BpayAmount ?? 0, ct);
        var regsInFlight = await db.BusinessNameRegistrations
            .Where(r => r.AirwallexTransferStatus != null && (r.AsicPaidAt >= since || (r.AsicPaidAt == null && !ended.Contains(r.AirwallexTransferStatus))))
            .Select(r => r.Years).ToListAsync(ct);
        return renewalsSpent + regsInFlight.Sum(y => asic.CurrentValue.FeeFor(y) ?? 0);
    }
}
