using Renewtron.Data;
using Renewtron.Services;
using static Renewtron.Services.RenewalOrderProjection;

namespace Renewtron.Tests;

/// <summary>Renewtron's own ASIC check (AsicCheckRules) and how a stored outcome sets the order's status.</summary>
public class AsicCheckRulesTests
{
    private static readonly DateOnly Register = new(2026, 11, 3);
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public void Still_registered_well_after_due_was_renewed_elsewhere()
    {
        var f = AsicCheckRules.FromRegister("Joe's Plumbing", Register.AddDays(-60), Register, ["JOES PLUMBING", "OTHER CO"]);
        Assert.Equal(RenewalOrderReason.RenewedElsewhere, f!.Outcome);
        Assert.Equal(OrderEvidenceSource.AsicRegister, f.Source);
        Assert.Contains("payment is kept", f.Evidence);
    }

    [Fact]
    public void Gone_from_the_register_well_after_due_has_lapsed_and_names_the_others()
    {
        var f = AsicCheckRules.FromRegister("Joe's Plumbing", Register.AddDays(-60), Register, ["JOE PLUMBING SERVICES PTY"]);
        Assert.Equal(RenewalOrderReason.Lapsed, f!.Outcome);
        Assert.Contains("JOE PLUMBING SERVICES PTY", f.Evidence);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(-48)]
    [InlineData(10)]
    public void Too_soon_after_due_the_register_cannot_tell(int dueDaysBeforeRegister) =>
        Assert.Null(AsicCheckRules.FromRegister("Joe's Plumbing", Register.AddDays(dueDaysBeforeRegister), Register, []));

    [Fact]
    public void Asic_offering_the_name_means_still_due()
    {
        var f = AsicCheckRules.FromRenewalPage("Joe's Plumbing", Today.AddDays(-5), Today, true, ["JOES PLUMBING"], null, true, ["JOES PLUMBING"]);
        Assert.True(f!.StillDue);
    }

    [Fact]
    public void Registered_overdue_and_not_offered_was_renewed_elsewhere()
    {
        var f = AsicCheckRules.FromRenewalPage("Joe's Plumbing", Today.AddDays(-5), Today, false, [], "This business name is not due for renewal", true, ["JOES PLUMBING"]);
        Assert.Equal(RenewalOrderReason.RenewedElsewhere, f!.Outcome);
        Assert.Equal(OrderEvidenceSource.AsicRenewalPage, f.Source);
    }

    [Fact]
    public void Registered_not_yet_due_and_not_offered_is_not_offered_now()
    {
        var f = AsicCheckRules.FromRenewalPage("Joe's Plumbing", Today.AddDays(20), Today, true, ["OTHER CO"], null, true, ["JOES PLUMBING", "OTHER CO"]);
        Assert.Equal(RenewalOrderReason.NotOfferedNow, f!.Outcome);
    }

    [Fact]
    public void Not_offered_and_not_on_the_register_but_others_are_is_a_name_mismatch()
    {
        var f = AsicCheckRules.FromRenewalPage("Joes Plumbing Services", Today.AddDays(-5), Today, true, ["J P SERVICES"], null, false, ["J P SERVICES"]);
        Assert.Equal(RenewalOrderReason.NameMismatch, f!.Outcome);
        Assert.Contains("J P SERVICES", f.Evidence);
    }

    [Fact]
    public void Nothing_under_the_abn_long_after_due_has_lapsed()
    {
        var f = AsicCheckRules.FromRenewalPage("Joe's Plumbing", Today.AddDays(-60), Today, false, [], "No business names found for this ABN", false, []);
        Assert.Equal(RenewalOrderReason.Lapsed, f!.Outcome);
    }

    [Theory]
    [InlineData("Failed to initialize renewal session")]
    [InlineData("A renewal is already in progress for this ABN")]
    public void No_answer_or_an_open_renewal_tells_nothing(string message) =>
        Assert.Null(AsicCheckRules.FromRenewalPage("Joe's Plumbing", Today.AddDays(-5), Today, false, [], message, true, []));

    [Theory]
    [InlineData(RenewalOrderReason.RenewedElsewhere, RenewalOrderStage.Closed)]
    [InlineData(RenewalOrderReason.RegisterAgain, RenewalOrderStage.Closed)]
    [InlineData(RenewalOrderReason.Lapsed, RenewalOrderStage.NeedsPerson)]
    [InlineData(RenewalOrderReason.NameMismatch, RenewalOrderStage.NeedsPerson)]
    [InlineData(RenewalOrderReason.NotOfferedNow, RenewalOrderStage.Waiting)]
    public void A_stored_outcome_sets_the_stage(RenewalOrderReason outcome, RenewalOrderStage stage)
    {
        var at = new DateTime(2026, 11, 12, 4, 0, 0, DateTimeKind.Utc);
        var r = Compute(new Source(SourceState.Failed, Today.AddDays(-5), "not found", Outcome: outcome, OutcomeAt: at, Evidence: "seen"), [], Today);
        Assert.Equal(stage, r.Stage);
        Assert.Equal(outcome, r.Reason);
        Assert.Equal("seen", r.Note);
    }

    [Fact]
    public void A_renewal_we_lodged_beats_a_stored_outcome()
    {
        var done = new Attempt(Guid.NewGuid(), RenewalStatus.Completed, new DateTime(2026, 11, 10), CompletedAt: new DateTime(2026, 11, 10));
        var r = Compute(new Source(SourceState.Queued, Today, Outcome: RenewalOrderReason.RenewedElsewhere, OutcomeAt: new DateTime(2026, 11, 11)), [done], Today);
        Assert.Equal(RenewalOrderStage.Renewed, r.Stage);
    }

    [Fact]
    public void A_newer_run_overrides_a_stop_for_a_person()
    {
        var run = new Attempt(Guid.NewGuid(), RenewalStatus.Processing, new DateTime(2026, 11, 12, 8, 0, 0));
        var r = Compute(new Source(SourceState.Queued, Today, Outcome: RenewalOrderReason.NameMismatch, OutcomeAt: new DateTime(2026, 11, 12, 4, 0, 0), LinkedAttemptId: run.Id), [run], Today);
        Assert.Equal(RenewalOrderStage.Renewing, r.Stage);
    }
}
