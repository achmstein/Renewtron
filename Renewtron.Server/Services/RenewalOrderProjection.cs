using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// The one place that decides a paid renewal's status. It works from what the pipeline stores
/// today: the paying record's own state (the Ontraport sale) and the ASIC runs for it.
/// Pure: no database, no clock except <c>today</c>, so every rule is unit-tested.
/// </summary>
public static class RenewalOrderProjection
{
    /// <summary>ASIC opens a name's renewal 29 days before it's due (David wants strictly under 30).</summary>
    public const int WindowDays = 29;

    /// <summary>What the paying record says, before any ASIC run.</summary>
    public enum SourceState
    {
        /// <summary>Paid, nothing started yet (waiting, queued for the next run, or re-checked daily).</summary>
        NotStarted,
        /// <summary>Handed to the renewal pipeline; the attempts say the rest.</summary>
        Queued,
        Completed,
        /// <summary>Failed before any renewal run (the ASIC name search).</summary>
        Failed,
        /// <summary>Ontraport says not to renew it (underpaid, disputed, cancelling…).</summary>
        Ineligible,
    }

    public sealed record Source(
        SourceState State,
        DateOnly? DueDate,
        string? Message = null,
        DateTime? HandledAt = null,
        string? HandledNote = null,
        /// <summary>ASIC's last search said "not due for renewal".</summary>
        bool AsicSaidNotDue = false,
        /// <summary>
        /// The run the paying record points at (the sale's RenewalRequestId; for wizard and manual
        /// renewals, the renewal itself). Older runs it let go of are history: they only count if
        /// one of them renewed the name.
        /// </summary>
        Guid? LinkedAttemptId = null,
        /// <summary>ASIC's name search didn't get an answer and is tried again on its own at this time.</summary>
        DateTime? NextSearchAt = null);

    public sealed record Attempt(
        Guid Id,
        RenewalStatus Status,
        DateTime InitiatedAt,
        string? ErrorCategory = null,
        string? ErrorMessage = null,
        DateTime? CompletedAt = null,
        DateTime? NextRetryAt = null,
        DateTime? HandledAt = null,
        string? HandledNote = null);

    public sealed record Result(
        RenewalOrderStage Stage,
        RenewalOrderReason? Reason = null,
        DateTime? NextActionAt = null,
        string? Note = null,
        Guid? CurrentAttemptId = null,
        DateTime? RenewedAt = null);

    public static Result Compute(Source source, IReadOnlyList<Attempt> attempts, DateOnly today)
    {
        // Renewed once is renewed: a later failed run (a duplicate) doesn't undo it.
        if (attempts.Where(a => a.Status == RenewalStatus.Completed).OrderBy(a => a.CompletedAt ?? a.InitiatedAt).FirstOrDefault() is { } done)
            return new(RenewalOrderStage.Renewed, CurrentAttemptId: done.Id, RenewedAt: done.CompletedAt ?? done.InitiatedAt);
        if (source.State == SourceState.Completed)
            return new(RenewalOrderStage.Renewed, CurrentAttemptId: Latest(attempts)?.Id);

        var current = source.LinkedAttemptId is { } linked ? attempts.FirstOrDefault(a => a.Id == linked) : null;

        // Settled outside Renewtron (the old "handled"), on the paying record or its last run.
        var handledAt = source.HandledAt ?? current?.HandledAt;
        if (handledAt != null)
            return new(RenewalOrderStage.Closed, RenewalOrderReason.RenewedElsewhere,
                Note: source.HandledNote ?? current?.HandledNote ?? "Marked handled.", CurrentAttemptId: current?.Id ?? Latest(attempts)?.Id);

        var overdue = source.DueDate is { } due && due < today;

        if (current is not null)
        {
            if (current.Status is RenewalStatus.Pending or RenewalStatus.Processing)
                return new(RenewalOrderStage.Renewing, CurrentAttemptId: current.Id);
            // Paid early: waiting for ASIC to open the renewal, checked again on that date.
            if (current.Status == RenewalStatus.Failed && current.ErrorCategory == RenewalErrorCategories.NotDueYet && current.NextRetryAt is { } check)
                return new(RenewalOrderStage.Waiting, NextActionAt: check, CurrentAttemptId: current.Id);
            if (current.NextRetryAt is { } retry)
                return new(RenewalOrderStage.Renewing, NextActionAt: retry, CurrentAttemptId: current.Id);
            return FromFailedAttempt(current, source, overdue, today);
        }

        switch (source.State)
        {
            case SourceState.Ineligible:
                return FromIneligible(source.Message);
            case SourceState.Failed when source.NextSearchAt is { } searchAgain:
                return new(RenewalOrderStage.Renewing, NextActionAt: searchAgain,
                    Note: "ASIC didn't answer the name search; Renewtron tries again on its own.");
            case SourceState.Failed:
                return new(RenewalOrderStage.NeedsPerson, NameNotFound(source.Message) ? RenewalOrderReason.NameNotFound : RenewalOrderReason.AttemptsExhausted,
                    Note: source.Message);
        }

        // Not started: ASIC saying "not due" after our due date has passed means someone renewed it.
        if (source.AsicSaidNotDue && overdue)
            return new(RenewalOrderStage.Closed, RenewalOrderReason.RenewedElsewhere,
                Note: "ASIC says it isn't due, though our due date has passed: renewed outside Renewtron.");
        if (source.DueDate is { } d && d.DayNumber - today.DayNumber > WindowDays)
            return new(RenewalOrderStage.Waiting, NextActionAt: d.AddDays(-WindowDays).ToDateTime(TimeOnly.MinValue));
        return new(RenewalOrderStage.Due);
    }

    private static Result FromFailedAttempt(Attempt a, Source source, bool overdue, DateOnly today)
    {
        Result Needs(RenewalOrderReason reason) => new(RenewalOrderStage.NeedsPerson, reason, Note: a.ErrorMessage, CurrentAttemptId: a.Id);
        switch (a.ErrorCategory)
        {
            case RenewalErrorCategories.PaymentRisk:
                return Needs(RenewalOrderReason.VerifyPaymentAtAsic);
            case RenewalErrorCategories.CardDeclined:
                return Needs(RenewalOrderReason.CardDeclined);
            case RenewalErrorCategories.AlreadyInProgress:
                return Needs(RenewalOrderReason.InProgressAtAsic);
            case RenewalErrorCategories.NotDueYet:
                if (overdue)
                    return new(RenewalOrderStage.Closed, RenewalOrderReason.RenewedElsewhere,
                        Note: "ASIC says it isn't due, though our due date has passed: renewed outside Renewtron.", CurrentAttemptId: a.Id);
                return source.DueDate is { } d && d.DayNumber - today.DayNumber > WindowDays
                    ? new(RenewalOrderStage.Waiting, NextActionAt: d.AddDays(-WindowDays).ToDateTime(TimeOnly.MinValue), CurrentAttemptId: a.Id)
                    : new(RenewalOrderStage.Due, Note: "ASIC said it wasn't due yet; try again.", CurrentAttemptId: a.Id);
            case RenewalErrorCategories.Terminal:
                return Needs(NameNotFound(a.ErrorMessage) ? RenewalOrderReason.NameNotFound : RenewalOrderReason.AsicRefused);
            default:
                // Transient (or unknown) with nothing scheduled: Renewtron has stopped trying.
                return Needs(RenewalOrderReason.AttemptsExhausted);
        }
    }

    private static Result FromIneligible(string? message)
    {
        var m = message ?? "";
        if (m.Contains("minimum renewal fee", StringComparison.OrdinalIgnoreCase))
            return new(RenewalOrderStage.NeedsPerson, RenewalOrderReason.BelowMinimumFee, Note: message);
        if (m.Contains("DISPUTE", StringComparison.OrdinalIgnoreCase))
            return new(RenewalOrderStage.Closed, RenewalOrderReason.Disputed, Note: message);
        if (m.Contains("refund", StringComparison.OrdinalIgnoreCase))
            return new(RenewalOrderStage.Closed, RenewalOrderReason.Refunded, Note: message);
        if (m.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
            return new(RenewalOrderStage.Closed, RenewalOrderReason.ClientCancelling, Note: message);
        return new(RenewalOrderStage.NeedsPerson, RenewalOrderReason.Ineligible, Note: message);
    }

    private static bool NameNotFound(string? message)
    {
        var m = message ?? "";
        return m.Contains("not found under ABN", StringComparison.OrdinalIgnoreCase)
            || m.Contains("No business names found", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Could not find business name", StringComparison.OrdinalIgnoreCase)
            || m.Contains("check the ABN", StringComparison.OrdinalIgnoreCase);
    }

    private static Attempt? Latest(IReadOnlyList<Attempt> attempts) =>
        attempts.OrderByDescending(a => a.InitiatedAt).FirstOrDefault();

    /// <summary>The paying sale's state, as the projection reads it.</summary>
    public static SourceState FromSale(OntraportSaleStatus status) => status switch
    {
        OntraportSaleStatus.RenewalCompleted => SourceState.Completed,
        OntraportSaleStatus.RenewalQueued => SourceState.Queued,
        OntraportSaleStatus.RenewalFailed => SourceState.Failed,
        OntraportSaleStatus.IneligibleForRenewal => SourceState.Ineligible,
        // Synced, waiting, ASIC not yet due / in progress (re-checked daily), and the old
        // "more than 30 days past due" give-up: none of them were started.
        _ => SourceState.NotStarted,
    };

}
