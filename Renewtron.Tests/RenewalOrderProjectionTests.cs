using Renewtron.Data;
using Renewtron.Services;
using static Renewtron.Services.RenewalOrderProjection;

namespace Renewtron.Tests;

/// <summary>Every rule that decides a paid renewal's status (RenewalOrderProjection.Compute).</summary>
public class RenewalOrderProjectionTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly DateTime Now = new(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);

    private static Attempt Run(RenewalStatus status, string? category = null, string? message = null,
        DateTime? nextRetry = null, DateTime? at = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), status, at ?? Now.AddDays(-1), category, message,
            status == RenewalStatus.Completed ? (at ?? Now.AddDays(-1)) : null, nextRetry);

    private static Source Sale(SourceState state, int dueInDays, string? message = null, Guid? linked = null, bool asicNotDue = false) =>
        new(state, Today.AddDays(dueInDays), message, AsicSaidNotDue: asicNotDue, LinkedAttemptId: linked);

    [Fact]
    public void Paid_early_waits_for_the_window()
    {
        var r = Compute(Sale(SourceState.NotStarted, dueInDays: 45), [], Today);
        Assert.Equal(RenewalOrderStage.Waiting, r.Stage);
        Assert.Equal(Today.AddDays(45 - WindowDays).ToDateTime(TimeOnly.MinValue), r.NextActionAt);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(0)]
    [InlineData(-40)] // the old "more than 30 days past due" give-up: still due, ASIC decides
    public void In_the_window_or_overdue_and_not_started_is_due(int dueInDays) =>
        Assert.Equal(RenewalOrderStage.Due, Compute(Sale(SourceState.NotStarted, dueInDays), [], Today).Stage);

    [Fact]
    public void A_completed_run_is_renewed_even_when_a_later_run_failed()
    {
        var done = Run(RenewalStatus.Completed, at: Now.AddDays(-10));
        var later = Run(RenewalStatus.Failed, RenewalErrorCategories.Transient, at: Now.AddDays(-2));
        var r = Compute(Sale(SourceState.Failed, -5, linked: later.Id), [done, later], Today);
        Assert.Equal(RenewalOrderStage.Renewed, r.Stage);
        Assert.Equal(done.Id, r.CurrentAttemptId);
    }

    [Fact]
    public void An_orphaned_failed_run_does_not_decide_when_the_sale_was_requeued()
    {
        var orphan = Run(RenewalStatus.Failed, RenewalErrorCategories.AlreadyInProgress);
        var r = Compute(Sale(SourceState.NotStarted, dueInDays: 10), [orphan], Today);
        Assert.Equal(RenewalOrderStage.Due, r.Stage);
    }

    [Fact]
    public void A_run_under_way_or_with_a_retry_scheduled_is_renewing()
    {
        var running = Run(RenewalStatus.Processing);
        Assert.Equal(RenewalOrderStage.Renewing, Compute(Sale(SourceState.Queued, 5, linked: running.Id), [running], Today).Stage);

        var retry = Run(RenewalStatus.Failed, RenewalErrorCategories.Transient, nextRetry: Now.AddHours(1));
        var r = Compute(Sale(SourceState.Failed, 5, linked: retry.Id), [retry], Today);
        Assert.Equal(RenewalOrderStage.Renewing, r.Stage);
        Assert.Equal(Now.AddHours(1), r.NextActionAt);
    }

    [Fact]
    public void A_wizard_renewal_paid_early_is_waiting_until_its_check_date()
    {
        var check = Now.AddDays(40);
        var run = Run(RenewalStatus.Failed, RenewalErrorCategories.NotDueYet, nextRetry: check);
        var r = Compute(new Source(SourceState.Queued, null, LinkedAttemptId: run.Id), [run], Today);
        Assert.Equal((RenewalOrderStage.Waiting, check), (r.Stage, r.NextActionAt));
    }

    [Theory]
    [InlineData(RenewalErrorCategories.PaymentRisk, null, RenewalOrderReason.VerifyPaymentAtAsic)]
    [InlineData(RenewalErrorCategories.CardDeclined, null, RenewalOrderReason.CardDeclined)]
    [InlineData(RenewalErrorCategories.AlreadyInProgress, null, RenewalOrderReason.InProgressAtAsic)]
    [InlineData(RenewalErrorCategories.Transient, null, RenewalOrderReason.AttemptsExhausted)]
    [InlineData(RenewalErrorCategories.Terminal, "Business name 'X' not found under ABN 123 at ASIC", RenewalOrderReason.NameNotFound)]
    [InlineData(RenewalErrorCategories.Terminal, "ASIC rejected the details", RenewalOrderReason.AsicRefused)]
    public void A_failed_run_with_nothing_scheduled_needs_a_person(string category, string? message, RenewalOrderReason reason)
    {
        var run = Run(RenewalStatus.Failed, category, message);
        var r = Compute(Sale(SourceState.Failed, -3, linked: run.Id), [run], Today);
        Assert.Equal(RenewalOrderStage.NeedsPerson, r.Stage);
        Assert.Equal(reason, r.Reason);
    }

    [Fact]
    public void Asic_saying_not_due_after_our_due_date_means_renewed_elsewhere()
    {
        var run = Run(RenewalStatus.Failed, RenewalErrorCategories.NotDueYet);
        var r = Compute(Sale(SourceState.Queued, -10, linked: run.Id), [run], Today);
        Assert.Equal((RenewalOrderStage.Closed, RenewalOrderReason.RenewedElsewhere), (r.Stage, r.Reason));

        var sale = Compute(Sale(SourceState.NotStarted, -10, asicNotDue: true), [], Today);
        Assert.Equal((RenewalOrderStage.Closed, RenewalOrderReason.RenewedElsewhere), (sale.Stage, sale.Reason));
    }

    [Fact]
    public void Asic_saying_not_due_before_our_due_date_waits_or_tries_again()
    {
        var run = Run(RenewalStatus.Failed, RenewalErrorCategories.NotDueYet);
        Assert.Equal(RenewalOrderStage.Waiting, Compute(Sale(SourceState.Queued, 60, linked: run.Id), [run], Today).Stage);
        Assert.Equal(RenewalOrderStage.Due, Compute(Sale(SourceState.Queued, 10, linked: run.Id), [run], Today).Stage);
    }

    [Fact]
    public void Handled_closes_as_renewed_elsewhere_with_its_note()
    {
        var src = new Source(SourceState.Failed, Today.AddDays(-90), "ASIC search failed", Now.AddDays(-1), "Still registered at ASIC");
        var r = Compute(src, [], Today);
        Assert.Equal((RenewalOrderStage.Closed, RenewalOrderReason.RenewedElsewhere), (r.Stage, r.Reason));
        Assert.Equal("Still registered at ASIC", r.Note);
    }

    [Theory]
    [InlineData("ASIC search failed: Search failed: The given header was not found.", RenewalOrderReason.AttemptsExhausted)]
    [InlineData("Business name 'ANYMAL' not found under ABN 64616730888 at ASIC", RenewalOrderReason.NameNotFound)]
    [InlineData("ASIC search failed: Failed to search for business names. Please check the ABN and try again.", RenewalOrderReason.NameNotFound)]
    public void A_sale_that_failed_before_any_run_needs_a_person(string message, RenewalOrderReason reason)
    {
        var r = Compute(Sale(SourceState.Failed, -5, message), [], Today);
        Assert.Equal((RenewalOrderStage.NeedsPerson, reason), (r.Stage, r.Reason!.Value));
    }

    [Theory]
    [InlineData("Amount paid (¤118.00) is below the minimum renewal fee (¤129.00)", RenewalOrderStage.NeedsPerson, RenewalOrderReason.BelowMinimumFee)]
    [InlineData("Stripe Payment Recieved is 'DISPUTE' (expected 'yes')", RenewalOrderStage.Closed, RenewalOrderReason.Disputed)]
    [InlineData("Contact has the Cancel field set — customer is cancelling, not renewing", RenewalOrderStage.Closed, RenewalOrderReason.ClientCancelling)]
    public void Ineligible_sales_by_reason(string message, RenewalOrderStage stage, RenewalOrderReason reason)
    {
        var r = Compute(Sale(SourceState.Ineligible, 5, message), [], Today);
        Assert.Equal((stage, reason), (r.Stage, r.Reason!.Value));
    }

    [Fact]
    public void Sale_statuses_map_to_source_states()
    {
        Assert.Equal(SourceState.NotStarted, FromSale(OntraportSaleStatus.NotDueForRenewal));
        Assert.Equal(SourceState.NotStarted, FromSale(OntraportSaleStatus.AsicNotYetDue));
        Assert.Equal(SourceState.Queued, FromSale(OntraportSaleStatus.RenewalQueued));
        Assert.Equal(SourceState.Failed, FromSale(OntraportSaleStatus.RenewalFailed));
        Assert.Equal(SourceState.Completed, FromSale(OntraportSaleStatus.RenewalCompleted));
    }
}
