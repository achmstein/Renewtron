using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>Why a renewal can't be re-run, or null when it can.</summary>
public enum RetryBlock { AlreadyCompleted, NotPaid, MayHoldPayment }

/// <summary>
/// The one rule for putting a renewal back in the queue, shared by the per-row and bulk retry so
/// they can't disagree. Never re-run a run that may already have paid or lodged at ASIC.
/// </summary>
public static class RenewalRetry
{
    public static RetryBlock? Check(RenewalRequest renewal)
    {
        if (renewal.Status == RenewalStatus.Completed) return RetryBlock.AlreadyCompleted;
        // Card payments must have gone through; External renewals were paid elsewhere.
        if (renewal.PaymentType == PaymentType.Stripe
            && renewal.StripePayment?.PaymentStatus != StripePaymentStatuses.Succeeded)
            return RetryBlock.NotPaid;
        // A run that may have paid ASIC, or lodged it to pay later by BPAY, could pay or lodge
        // twice. These need checking at ASIC first.
        if (renewal.FailedAtStep == "Submit Pay Later"
            || RenewalErrorClassifier.MayHoldPayment(renewal.FailedAtStep, renewal.HostedTokenizationId, renewal.ErrorCategory))
            return RetryBlock.MayHoldPayment;
        return null;
    }

    /// <summary>Back to Pending. Keeps ErrorMessage/FailedAtStep: the next run overwrites them,
    /// and until then the operator can still see why the last attempt failed.</summary>
    public static void Requeue(RenewalRequest renewal)
    {
        renewal.Status = RenewalStatus.Pending;
        renewal.NextRetryAt = null;
        renewal.HandledAt = null;
        renewal.HandledNote = null;
    }
}
