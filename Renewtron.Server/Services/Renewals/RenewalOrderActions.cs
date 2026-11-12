using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>What a person's action on one paid renewal came to: done, or why not, in a sentence.</summary>
public sealed record OrderActionResult(bool Ok, string Message)
{
    public static OrderActionResult Done(string message) => new(true, message);
    public static OrderActionResult No(string why) => new(false, why);
}

/// <summary>
/// What staff can do to a paid renewal, one order at a time. The admin's single-renewal buttons and
/// its bulk bar both come here, so a renewal acted on alone or with others gets the same checks. None
/// of these refresh the orders: the caller does that once, after all of them.
/// </summary>
public static class RenewalOrderActions
{
    /// <summary>The outcomes a person can record.</summary>
    public static readonly RenewalOrderReason[] PersonOutcomes =
    [
        RenewalOrderReason.RenewedElsewhere, RenewalOrderReason.Refunded, RenewalOrderReason.RegisterAgain,
        RenewalOrderReason.ClientCancelling, RenewalOrderReason.Disputed, RenewalOrderReason.Duplicate,
    ];

    /// <summary>A person's decision on how the order ends, with the note and reference it rests on. Saved by the caller.</summary>
    public static OrderActionResult Decide(RenewalOrder order, RenewalOrderReason outcome, string? note, string? reference, string by)
    {
        if (!PersonOutcomes.Contains(outcome))
            return OrderActionResult.No("Choose renewed elsewhere, refunded, register again, client cancelled, disputed or duplicate.");
        if (order.Stage == RenewalOrderStage.Renewed)
            return OrderActionResult.No("Renewtron renewed this one; it can't be closed another way.");
        if (order.Stage == RenewalOrderStage.Renewing)
            return OrderActionResult.No("It's renewing at ASIC right now. Wait for the run to finish.");
        note = note?.Trim();
        var agreeing = order.Outcome == outcome && order.Evidence != null;
        if (string.IsNullOrEmpty(note) && !agreeing)
            return OrderActionResult.No("Say what this rests on (what ASIC showed, or what the client said).");

        // A person confirming the ASIC check's finding keeps its evidence and adds their note.
        if (agreeing)
        {
            if (!string.IsNullOrEmpty(note)) order.Evidence = Trim($"{order.Evidence} {by}: {note}", 1000);
        }
        else
        {
            order.Evidence = Trim(note, 1000);
            order.EvidenceSource = OrderEvidenceSource.Staff;
            order.EvidenceAt = DateTime.UtcNow;
        }
        order.Outcome = outcome;
        order.OutcomeAt = DateTime.UtcNow;
        order.OutcomeBy = by;
        order.OutcomeReference = Trim(reference?.Trim(), 200) is { Length: > 0 } r ? r : null;
        return OrderActionResult.Done("Saved.");
    }

    /// <summary>Undoes a decision: the pipeline decides the status again. Saved by the caller.</summary>
    public static OrderActionResult Reopen(RenewalOrder order)
    {
        if (order.Outcome is null) return OrderActionResult.No("Nothing to undo.");
        AsicCheckService.ClearOutcome(order);
        return OrderActionResult.Done("Reopened.");
    }

    /// <summary>
    /// Try again: retries the current ASIC run, or (when the sale never got that far) puts the sale
    /// back for the next processing run. Only for reasons where no money can be at risk. Saves.
    /// </summary>
    public static async Task<OrderActionResult> RetryAsync(ApplicationDbContext db, IBackgroundJobClient jobs, RenewalOrder order, CancellationToken ct)
    {
        if (order.Stage != RenewalOrderStage.NeedsPerson || order.Reason is not { } reason || !WorkQueues.OrderTryAgain.Contains(reason))
            return OrderActionResult.No("This renewal can't simply be tried again: check it first.");
        // Never renew a name ASIC already shows renewed: ASIC says "not due", and the order would
        // wait and renew it a second time next year.
        if (order.ClientBusinessNameId is { } nid && order.DueDate is { } orderDue
            && await db.ClientBusinessNames.AnyAsync(n => n.Id == nid && n.RenewalDueDate > orderDue
                && (n.DueDateSource == RenewalDueDateSource.AsicRegister || n.DueDateSource == RenewalDueDateSource.Estimated), ct))
            return OrderActionResult.No("ASIC shows this name renewed since. Close it as renewed elsewhere instead.");

        var attempt = order.CurrentAttemptId is { } aid ? await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == aid, ct) : null;
        if (attempt is not null && attempt.Status == RenewalStatus.Failed)
        {
            if (attempt.NextRetryAt != null)
                return OrderActionResult.No("It is already scheduled to try again.");
            if (RenewalErrorClassifier.MayHoldPayment(attempt.FailedAtStep, attempt.HostedTokenizationId, attempt.ErrorCategory))
                return OrderActionResult.No("ASIC may already hold money for it. Check at ASIC first.");
            attempt.Status = RenewalStatus.Pending;
            attempt.HandledAt = null;
            attempt.HandledNote = null;
            if (order.Source == RenewalOrderSource.Ontraport
                && await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is { } sale)
            {
                sale.Status = OntraportSaleStatus.RenewalQueued;
                sale.RenewalRequestId = attempt.Id;
            }
            await db.SaveChangesAsync(ct);
            var attemptId = attempt.Id;
            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(attemptId));
            return OrderActionResult.Done("Trying again now.");
        }
        if (order.Source == RenewalOrderSource.Ontraport
            && await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is { } queued)
        {
            Requeue(queued);
            await db.SaveChangesAsync(ct);
            return OrderActionResult.Done("Back in the queue: the next processing run (07:00) renews it.");
        }
        return OrderActionResult.No("Nothing to try again for this renewal.");
    }

    /// <summary>The name we hold doesn't match ASIC's: corrects it on the sale and puts it back in the queue. Saved by the caller.</summary>
    public static async Task<OrderActionResult> FixNameAsync(ApplicationDbContext db, RenewalOrder order, string businessName, CancellationToken ct)
    {
        var name = BusinessNameText.Clean(businessName);
        if (name.Length is 0 or > 200) return OrderActionResult.No("Enter the name exactly as ASIC shows it.");
        if (order.Source != RenewalOrderSource.Ontraport
            || await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is not { } sale)
            return OrderActionResult.No("Only an Ontraport sale's name can be corrected here.");
        if (await RenewingAsync(db, sale, ct))
            return OrderActionResult.No("It's renewing at ASIC right now.");

        sale.BusinessName = name;
        Requeue(sale);
        sale.HandledAt = null;
        sale.HandledNote = null;
        AsicCheckService.ClearOutcome(order);
        return OrderActionResult.Done("Name corrected. The next processing run (07:00) renews it.");
    }

    /// <summary>
    /// A person found the name's due date (at ASIC, or from the client) when neither Ontraport nor
    /// ASIC's records gave one: puts it on the sale, which goes back in the queue. The processing run
    /// then renews it, waits for the window, or calls it too late, as it would for any sale. Saved by the caller.
    /// </summary>
    public static async Task<OrderActionResult> SetDueDateAsync(ApplicationDbContext db, RenewalOrder order, DateOnly due, DateOnly today, CancellationToken ct)
    {
        if (due < today.AddYears(-5) || due > today.AddYears(4))
            return OrderActionResult.No("That date doesn't look right: check it at ASIC.");
        if (order.Stage is RenewalOrderStage.Renewed or RenewalOrderStage.Renewing or RenewalOrderStage.Closed)
            return OrderActionResult.No("Only an open renewal's due date can be set.");
        if (order.Source != RenewalOrderSource.Ontraport
            || await db.OntraportSales.FirstOrDefaultAsync(s => s.Id == order.SourceId, ct) is not { } sale)
            return OrderActionResult.No("Only an Ontraport sale's due date can be set here.");
        if (await RenewingAsync(db, sale, ct))
            return OrderActionResult.No("It's renewing at ASIC right now.");

        sale.RenewalDueDate = due.ToDateTime(new TimeOnly(2, 0), DateTimeKind.Utc);
        if (sale.ErrorMessage is OntraportSalesService.NoDueDateMessage or OntraportSalesService.TooLateMessage) sale.ErrorMessage = null;
        if (sale.RenewalRequestId is null && sale.HandledAt is null) Requeue(sale);

        var days = due.DayNumber - today.DayNumber;
        return OrderActionResult.Done(days switch
        {
            > 29 => $"Due {due:d MMM yyyy}. It renews on its own when ASIC opens the window, {due.AddDays(-29):d MMM}.",
            < -RenewalOrderProjection.TooLateDays => $"Due {due:d MMM yyyy}: too late to renew online. Refund, or register it again.",
            _ => $"Due {due:d MMM yyyy}. The next processing run (07:00) renews it.",
        });
    }

    private static void Requeue(OntraportSale sale)
    {
        sale.Status = OntraportSaleStatus.Synced;
        sale.RenewalRequestId = null;
        sale.ProcessedAt = null;
        sale.ErrorMessage = null;
        sale.SearchAttempts = 0;
        sale.NextSearchAt = null;
    }

    private static Task<bool> RenewingAsync(ApplicationDbContext db, OntraportSale sale, CancellationToken ct) =>
        sale.RenewalRequestId is { } rid
            ? db.RenewalRequests.AnyAsync(r => r.Id == rid && (r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing), ct)
            : Task.FromResult(false);

    private static string? Trim(string? s, int max) => s is { Length: > 0 } && s.Length > max ? s[..max] : s;
}
