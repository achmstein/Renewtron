using System.Linq.Expressions;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// The admin's work queues, defined once so the Today page, sidebar badges, list filters and stats
/// agree on what "needs a person" means. The admin's status vocabulary
/// (frontend/src/admin/_status.tsx) reads the same way.
/// </summary>
public static class WorkQueues
{
    /// <summary>Paid renewals a person can push through by trying again (no money at risk).</summary>
    public static readonly RenewalOrderReason[] OrderTryAgain =
        [RenewalOrderReason.CardDeclined, RenewalOrderReason.AttemptsExhausted, RenewalOrderReason.InProgressAtAsic];

    /// <summary>Paid renewals to look up at ASIC before anything else: ASIC may hold our money, or doesn't list the name.</summary>
    public static readonly RenewalOrderReason[] OrderCheckFirst =
        [RenewalOrderReason.VerifyPaymentAtAsic, RenewalOrderReason.NameNotFound, RenewalOrderReason.NameMismatch, RenewalOrderReason.AsicRefused, RenewalOrderReason.NoDueDate];

    /// <summary>Paid renewals waiting on a decision: renew anyway, refund, or (lapsed) offer to register it again.</summary>
    public static readonly RenewalOrderReason[] OrderDecide =
        [RenewalOrderReason.BelowMinimumFee, RenewalOrderReason.Ineligible, RenewalOrderReason.TooLate, RenewalOrderReason.Lapsed];

    /// <summary>A paid renewal stopped for a person (<see cref="RenewalOrderStage.NeedsPerson"/>) in one of the groups above.</summary>
    public static IQueryable<RenewalOrder> OrdersIn(this IQueryable<RenewalOrder> q, RenewalOrderReason[] reasons) =>
        q.Where(o => o.Stage == RenewalOrderStage.NeedsPerson && o.Reason != null && reasons.Contains(o.Reason.Value));

    /// <summary>A paid renewal whose ASIC run has been queued or lodging for over <see cref="StuckAfter"/>.</summary>
    public static IQueryable<RenewalOrder> OrdersStuck(this IQueryable<RenewalOrder> q, ApplicationDbContext db, DateTime now)
    {
        var cutoff = now - StuckAfter;
        return q.Where(o => o.Stage == RenewalOrderStage.Renewing && db.RenewalRequests.Any(r => r.Id == o.CurrentAttemptId
            && (r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing)
            && (r.LastAttemptAt ?? r.InitiatedAt) < cutoff));
    }

    /// <summary>
    /// A renewal the client has paid for that failed, and nothing will retry on its own: no retry
    /// scheduled, and not turned away only because ASIC's renewal window hasn't opened. A Stripe
    /// renewal whose card payment never succeeded isn't here: there's no money of theirs to deliver.
    /// </summary>
    public static readonly Expression<Func<RenewalRequest, bool>> RenewalNeedsPerson = r =>
        r.Status == RenewalStatus.Failed
        && r.HandledAt == null
        && r.NextRetryAt == null
        && (r.ErrorCategory == null || r.ErrorCategory != RenewalErrorCategories.NotDueYet)
        && (r.PaymentType == PaymentType.External
            || (r.StripePayment != null && r.StripePayment.PaymentStatus == StripePaymentStatuses.Succeeded));

    /// <summary>How long a renewal may sit queued or lodging before we call it stuck.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromHours(2);

    /// <summary>Queued or lodging, and nothing has happened to it for <see cref="StuckAfter"/>.</summary>
    public static Expression<Func<RenewalRequest, bool>> RenewalStuck(DateTime now)
    {
        var cutoff = now - StuckAfter;
        return r => (r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing)
            && r.HandledAt == null
            && (r.LastAttemptAt ?? r.InitiatedAt) < cutoff;
    }

    private static readonly List<string> BpayPayableStatuses =
        BpayPaymentService.Ended.Append(BpayPaymentService.Unknown).ToList();

    /// <summary>
    /// An ASIC BPAY bill we can pay now: lodged to pay by BPAY, unpaid, no transfer under way.
    /// The SQL form of <see cref="BpayPaymentService.IsPayable"/>.
    /// </summary>
    public static readonly Expression<Func<RenewalRequest, bool>> BpayToPay = r =>
        !r.IsTest && r.BpayReference != null && r.BpayReference != ""
        && r.BpayBillerCode != null && r.BpayBillerCode != "" && r.BpayPaidAt == null
        && (r.AirwallexTransferStatus == null || BpayPayableStatuses.Contains(r.AirwallexTransferStatus));

    /// <summary>Key requests a person still has to send on ASIC's form (the key tool's /manual list).</summary>
    public static readonly AsicKeyRequestStatus[] KeyRequestOpen =
    [
        AsicKeyRequestStatus.Pending,
        AsicKeyRequestStatus.Failed,
        AsicKeyRequestStatus.Manual,
    ];

    /// <summary>
    /// Key letters that need no one: read and saved (to Ontraport or to the client), or still
    /// being read. Every other status needs a person.
    /// </summary>
    public static readonly AsicKeyNotificationStatus[] KeyLetterSettled =
    [
        AsicKeyNotificationStatus.Pending,
        AsicKeyNotificationStatus.Completed,
        AsicKeyNotificationStatus.SavedToClient,
    ];
}
