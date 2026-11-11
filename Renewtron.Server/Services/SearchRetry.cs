namespace Renewtron.Services;

/// <summary>
/// When to try ASIC's name search again after it failed to get through (ASIC down, session
/// refused, network). The search runs before any renewal exists, so before this it had no retry
/// at all: 260 paid renewals sat in July–August 2026 because one search failed.
/// </summary>
public static class SearchRetry
{
    public const int MaxAttempts = 6;

    /// <summary>The wait after the <paramref name="failures"/>th failure; null once it should stop.</summary>
    public static TimeSpan? Delay(int failures) => failures switch
    {
        <= 0 => TimeSpan.Zero,
        1 => TimeSpan.FromHours(1),
        2 => TimeSpan.FromHours(3),
        3 => TimeSpan.FromHours(12),
        < MaxAttempts => TimeSpan.FromHours(24),
        _ => null,
    };

    /// <summary>
    /// The search got an answer from ASIC (names, "not due", "in progress", or none under the
    /// ABN): no point asking again. Everything else is ASIC not answering.
    /// </summary>
    public static bool AsicAnswered(bool success, string? errorMessage) =>
        success
        || (errorMessage ?? "").Contains("not due for renewal", StringComparison.OrdinalIgnoreCase)
        || (errorMessage ?? "").Contains("already in progress", StringComparison.OrdinalIgnoreCase)
        || (errorMessage ?? "").Contains("We are processing", StringComparison.OrdinalIgnoreCase)
        || (errorMessage ?? "").Contains("No business names found", StringComparison.OrdinalIgnoreCase)
        || (errorMessage ?? "").Contains("check the ABN", StringComparison.OrdinalIgnoreCase);
}
