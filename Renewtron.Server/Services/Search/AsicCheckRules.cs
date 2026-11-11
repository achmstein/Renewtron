using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// What ASIC's own records say about a paid renewal Renewtron hasn't finished, from the two
/// sources it reads without help: ASIC's public register file (data.gov.au, imported nightly) and
/// ASIC's renewal page (what it offers for renewal under an ABN). Pure, so every rule is tested.
///
/// The register has no due dates (ASIC dropped them in 2025), but it does show which names are
/// still registered. A name gone from the register more than <see cref="CancelGraceDays"/> after
/// its due date has lapsed. A name still registered may or may not have been renewed (ASIC often
/// leaves unrenewed names registered for a long time): only ASIC's own due date, from its register
/// search, moving past the order's shows a renewal.
/// </summary>
public static class AsicCheckRules
{
    /// <summary>The longest ASIC was seen to wait before cancelling an unrenewed name (measured Nov 2026).</summary>
    public const int CancelGraceDays = 48;

    public sealed record Finding(RenewalOrderReason? Outcome, string Evidence, OrderEvidenceSource Source)
    {
        /// <summary>ASIC still offers the name: any earlier "not offered" or "wrong name" no longer holds.</summary>
        public bool StillDue => Outcome is null;
    }

    /// <summary>A renewal moves ASIC's due date on by at least a year; this much later than the order's means it was renewed.</summary>
    public const int RenewedAfterDays = 180;

    /// <summary>
    /// What the register file says, or null when it can't tell (too soon after the due date, or the
    /// name is still registered and ASIC's own due date for it isn't known to have moved on).
    /// Still being registered proves nothing: ASIC leaves unrenewed names registered for months,
    /// even years, past their due date (seen across 947 names in Nov 2026). Only ASIC's due date
    /// moving past the one the order paid for shows it was renewed.
    /// </summary>
    /// <param name="registerNames">Every registered name under the order's ABN in the register file.</param>
    /// <param name="asicDue">The name's due date as ASIC's register search last showed it, if known.</param>
    public static Finding? FromRegister(string businessName, DateOnly due, DateOnly registerDate, IReadOnlyList<string> registerNames, DateOnly? asicDue = null)
    {
        var registered = BusinessNameText.FindName(registerNames, n => n, businessName) is not null;
        if (registered && asicDue is { } next && next.DayNumber - due.DayNumber > RenewedAfterDays)
            return new(RenewalOrderReason.RenewedElsewhere,
                $"ASIC's register search shows it next due on {next:d MMM yyyy}, after the {due:d MMM yyyy} this order paid for: "
                + "it was renewed outside Renewtron. The payment is kept.",
                OrderEvidenceSource.AsicRegister);

        var settled = due.AddDays(CancelGraceDays) < registerDate;
        if (!settled || registered) return null;
        var days = registerDate.DayNumber - due.DayNumber;
        return new(RenewalOrderReason.Lapsed,
            $"Not on ASIC's register of {registerDate:d MMM yyyy}, {days} days after it was due on {due:d MMM yyyy}: ASIC cancelled it."
            + OtherNames(registerNames, "under this ABN on the register"),
            OrderEvidenceSource.AsicRegister);
    }

    /// <summary>What ASIC's renewal page answered for the ABN, or null when it didn't answer (or a renewal is open there).</summary>
    /// <param name="onRegister">Whether the register file lists the name under the ABN; null when no register is loaded.</param>
    public static Finding? FromRenewalPage(string businessName, DateOnly due, DateOnly today, bool searchSucceeded,
        IReadOnlyList<string> offeredNames, string? errorMessage, bool? onRegister, IReadOnlyList<string> registerNames)
    {
        var message = errorMessage ?? "";
        if (!SearchRetry.AsicAnswered(searchSucceeded, errorMessage)) return null;
        if (message.Contains("already in progress", StringComparison.OrdinalIgnoreCase)
            || message.Contains("We are processing", StringComparison.OrdinalIgnoreCase))
            return null;

        var asked = $"ASIC's renewal page on {today:d MMM yyyy}";
        if (searchSucceeded && BusinessNameText.FindName(offeredNames, n => n, businessName) is not null)
            return new(null, $"{asked} offers it for renewal.", OrderEvidenceSource.AsicRenewalPage);

        var stillRegistered = onRegister ?? true;
        if (!stillRegistered)
        {
            var asicNames = searchSucceeded ? offeredNames : registerNames;
            // Nothing at all under the ABN, long after the due date: the name is gone.
            if (asicNames.Count == 0 && due.AddDays(CancelGraceDays) < today)
                return new(RenewalOrderReason.Lapsed,
                    $"{asked} lists no names under the ABN and the register doesn't have it, {today.DayNumber - due.DayNumber} days after it was due: ASIC cancelled it.",
                    OrderEvidenceSource.AsicRenewalPage);
            return new(RenewalOrderReason.NameMismatch,
                $"{asked} doesn't list this name under the ABN, and the register doesn't either."
                + OtherNames(asicNames, searchSucceeded ? "ASIC offers" : "on the register under this ABN"),
                OrderEvidenceSource.AsicRenewalPage);
        }

        // Registered, but ASIC isn't offering it: renewed already if it was due, otherwise not yet open.
        if (due < today)
            return new(RenewalOrderReason.RenewedElsewhere,
                $"{asked} doesn't offer it for renewal though it was due on {due:d MMM yyyy}, and it's still registered: renewed outside Renewtron. The payment is kept.",
                OrderEvidenceSource.AsicRenewalPage);
        return new(RenewalOrderReason.NotOfferedNow,
            $"{asked} isn't offering it for renewal yet (due {due:d MMM yyyy}). Renewtron looks again in a week.",
            OrderEvidenceSource.AsicRenewalPage);
    }

    private static string OtherNames(IReadOnlyList<string> names, string where) =>
        names.Count == 0 ? "" : $" Names {where}: {string.Join(", ", names.Take(5))}{(names.Count > 5 ? $" and {names.Count - 5} more" : "")}.";
}
