using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

public class RenewalRetryTests
{
    private static RenewalRequest Failed(PaymentType type = PaymentType.Stripe, string? paymentStatus = StripePaymentStatuses.Succeeded) => new()
    {
        Id = Guid.NewGuid(),
        Status = RenewalStatus.Failed,
        PaymentType = type,
        StripePayment = paymentStatus is null ? null : new StripePayment { PaymentIntentId = "pi", PaymentStatus = paymentStatus },
    };

    [Fact]
    public void A_paid_failed_renewal_can_be_retried() => Assert.Null(RenewalRetry.Check(Failed()));

    [Fact]
    public void An_external_renewal_needs_no_card_payment() =>
        Assert.Null(RenewalRetry.Check(Failed(PaymentType.External, paymentStatus: null)));

    [Fact]
    public void An_unpaid_card_renewal_is_blocked() =>
        Assert.Equal(RetryBlock.NotPaid, RenewalRetry.Check(Failed(paymentStatus: "requires_payment_method")));

    [Fact]
    public void A_completed_renewal_is_blocked()
    {
        var r = Failed();
        r.Status = RenewalStatus.Completed;
        Assert.Equal(RetryBlock.AlreadyCompleted, RenewalRetry.Check(r));
    }

    [Theory]
    [InlineData("Submit Pay Later", null)]
    [InlineData("Complete Payment Action", null)]
    [InlineData("Some step", "hosted-123")]
    public void A_run_that_may_have_paid_ASIC_is_blocked(string step, string? hostedTokenizationId)
    {
        var r = Failed();
        r.FailedAtStep = step;
        r.HostedTokenizationId = hostedTokenizationId;
        Assert.Equal(RetryBlock.MayHoldPayment, RenewalRetry.Check(r));
    }

    [Fact]
    public void Requeue_clears_the_handled_mark_but_keeps_the_last_error()
    {
        var r = Failed();
        r.ErrorMessage = "ASIC timed out";
        r.HandledAt = DateTime.UtcNow;
        r.NextRetryAt = DateTime.UtcNow;

        RenewalRetry.Requeue(r);

        Assert.Equal(RenewalStatus.Pending, r.Status);
        Assert.Null(r.HandledAt);
        Assert.Null(r.NextRetryAt);
        Assert.Equal("ASIC timed out", r.ErrorMessage);
    }
}
