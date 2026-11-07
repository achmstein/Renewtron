using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>Paying early: when a renewal ASIC isn't taking yet is looked at again.</summary>
public class EarlyRenewalTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Theory]
    [InlineData("15/03/2020", "2027-03-15")]   // anniversary later this cycle
    [InlineData("07/11/2019", "2026-11-07")]   // due today
    [InlineData("20/11/2024", "2026-11-20")]   // inside the window
    [InlineData("01/01/2026", "2027-01-01")]   // registered this year
    [InlineData("29/02/2024", "2027-02-28")]   // leap day
    public void Due_date_is_the_next_registration_anniversary(string registered, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), EarlyRenewalService.EstimatedDueDate(Today, registered));

    [Fact]
    public void Unreadable_registration_date_has_no_due_date() =>
        Assert.Null(EarlyRenewalService.EstimatedDueDate(Today, "soon"));

    [Fact]
    public void Before_the_window_checks_on_the_day_it_opens() =>
        // Due 15/03/2027: ASIC opens it 29 days before.
        Assert.Equal(new DateOnly(2027, 2, 14), EarlyRenewalService.NextCheck(Today, "15/03/2020"));

    [Fact]
    public void Inside_the_window_checks_again_tomorrow() =>
        Assert.Equal(Today.AddDays(1), EarlyRenewalService.NextCheck(Today, "20/11/2024"));

    [Fact]
    public void No_registration_date_checks_in_a_week() =>
        Assert.Equal(Today.AddDays(7), EarlyRenewalService.NextCheck(Today, null));

    [Fact]
    public void Waiting_renewal_is_marked_and_recognised()
    {
        var r = new RenewalRequest();
        EarlyRenewalService.MarkWaiting(r, new DateOnly(2027, 2, 14));
        Assert.True(EarlyRenewalService.IsWaiting(r.Status, r.ErrorCategory, r.NextRetryAt));
        Assert.Equal(new DateTime(2027, 2, 14, 0, 0, 0, DateTimeKind.Utc), r.NextRetryAt);
        Assert.Equal(RenewalErrorCategories.NotDueYet, r.ErrorCategory);
    }

    [Fact]
    public void Customer_is_never_told_a_waiting_renewal_is_not_due()
    {
        var m = RenewalErrorClassifier.CustomerMessage(RenewalStatus.Failed, RenewalErrorCategories.NotDueYet, DateTime.UtcNow);
        Assert.DoesNotContain("due", m!, StringComparison.OrdinalIgnoreCase);
    }
}
