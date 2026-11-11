using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// An Ontraport sale's due-date field, read correctly. When we renew a sold name, Ontraport's
/// field is usually moved on to the next due date, but not always (about a third of completed
/// sales still hold the date that was renewed). A completed sale's date that lies well after
/// the sale was paid has been moved on; one around the sale is the date that was renewed.
/// </summary>
public static class SaleDueDates
{
    /// <summary>A due date this long after the sale was paid has been moved on already: renewals are at least a year.</summary>
    private const int RolledAfterDays = 300;

    public static bool IsRolled(OntraportSaleStatus status, DateOnly due, DateTime paidAt) =>
        status == OntraportSaleStatus.RenewalCompleted && due.DayNumber - DateOnly.FromDateTime(paidAt).DayNumber > RolledAfterDays;

    /// <summary>The due date the sale paid to renew.</summary>
    public static DateOnly Renewed(OntraportSaleStatus status, DateOnly due, DateTime paidAt, int years) =>
        IsRolled(status, due, paidAt) ? due.AddYears(-Math.Max(1, years)) : due;

    /// <summary>The name's next due date once the sale's renewal is done.</summary>
    public static DateOnly NextAfterRenewal(OntraportSaleStatus status, DateOnly due, DateTime paidAt, int years) =>
        IsRolled(status, due, paidAt) ? due : due.AddYears(Math.Max(1, years));
}
