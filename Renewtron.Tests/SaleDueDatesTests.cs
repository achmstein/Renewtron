using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Tests;

/// <summary>Ontraport's due-date field on a completed sale is sometimes already moved on, sometimes not.</summary>
public class SaleDueDatesTests
{
    private static readonly DateTime Paid = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_date_moved_on_by_the_renewal_is_not_moved_again()
    {
        // JZ Civil: renewed in Aug 2026 for 3 years; Ontraport already shows Sept 2029.
        var due = new DateOnly(2029, 9, 19);
        Assert.Equal(due, SaleDueDates.NextAfterRenewal(OntraportSaleStatus.RenewalCompleted, due, Paid, 3));
        Assert.Equal(new DateOnly(2026, 9, 19), SaleDueDates.Renewed(OntraportSaleStatus.RenewalCompleted, due, Paid, 3));
    }

    [Fact]
    public void A_date_still_showing_the_renewed_term_moves_on_by_the_years_paid()
    {
        var due = new DateOnly(2026, 9, 19);
        Assert.Equal(new DateOnly(2029, 9, 19), SaleDueDates.NextAfterRenewal(OntraportSaleStatus.RenewalCompleted, due, Paid, 3));
        Assert.Equal(due, SaleDueDates.Renewed(OntraportSaleStatus.RenewalCompleted, due, Paid, 3));
    }

    [Fact]
    public void A_sale_not_yet_renewed_keeps_its_date() =>
        Assert.Equal(new DateOnly(2027, 9, 19), SaleDueDates.Renewed(OntraportSaleStatus.WaitingForRenewalWindow, new DateOnly(2027, 9, 19), Paid, 1));
}
