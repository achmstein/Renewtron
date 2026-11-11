using System.Globalization;

namespace Asic.Integrations.Registry;

public sealed record AsicHolder(string? Name, string? Type, string? Abn);

/// <summary>One name's detail page on ASIC's register search. Dates are ASIC's dd/MM/yyyy text; see <see cref="AsicDates"/>.</summary>
public sealed record AsicRegistryBusinessName(
    string? Name,
    string? Status,
    string? RegistrationDate,
    string? RenewalDate,
    string? CancelledDate,
    string? CancellationUnderReview,
    string? AddressForServiceDocuments,
    string? PrincipalPlaceOfBusiness,
    IReadOnlyList<AsicHolder> Holders);

public sealed record AsicRegistrySearchResult(bool Succeeded, IReadOnlyList<AsicRegistryBusinessName> BusinessNames, bool HasMultiple = false, string? Reason = null)
{
    public static AsicRegistrySearchResult Fail(string? reason = null) => new(false, [], false, reason);
    public static AsicRegistrySearchResult Ok(IReadOnlyList<AsicRegistryBusinessName> names, bool hasMultiple = false) => new(true, names, hasMultiple);
}

/// <summary>A 2Captcha account balance, or why it couldn't be read (bad key, network).</summary>
public sealed record CaptchaBalanceResult(decimal? Balance, string? Error);

/// <summary>A CAPTCHA solve: a token, or why there isn't one (zero balance, bad key, timeout).</summary>
public sealed record CaptchaSolveResult(string? Token, string? Error)
{
    public bool Succeeded => !string.IsNullOrEmpty(Token);
    public static CaptchaSolveResult Ok(string token) => new(token, null);
    public static CaptchaSolveResult Fail(string error) => new(null, error);
}

/// <summary>Reads ASIC's date text (day first). Ported from Businesstron's RenewalWindow.TryParse.</summary>
public static class AsicDates
{
    private static readonly string[] Formats =
    [
        "dd/MM/yyyy", "d/MM/yyyy", "dd/M/yyyy", "d/M/yyyy",
        "dd-MM-yyyy", "yyyy-MM-dd", "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy",
    ];

    public static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(trimmed, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        return DateOnly.TryParse(trimmed, CultureInfo.GetCultureInfo("en-AU"), DateTimeStyles.None, out date)
            || DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static DateOnly? Parse(string? value) => TryParse(value, out var d) ? d : null;
}
