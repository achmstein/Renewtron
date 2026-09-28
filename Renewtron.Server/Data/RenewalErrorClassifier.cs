namespace Renewtron.Data;

/// <summary>
/// Categories a failed renewal falls into. Stored on RenewalRequest.ErrorCategory and
/// used to decide whether the failure retries automatically or needs a human.
/// </summary>
public static class RenewalErrorCategories
{
    /// <summary>Network blip, ASIC maintenance page, unexpected exception — retry with backoff.</summary>
    public const string Transient = "Transient";

    /// <summary>ASIC says the name isn't due yet — timing, not an error. The Ontraport sale loop re-attempts when due.</summary>
    public const string NotDueYet = "NotDueYet";

    /// <summary>ASIC reports an open renewal session for this ABN — recheck later.</summary>
    public const string AlreadyInProgress = "AlreadyInProgress";

    /// <summary>The run got to (or past) payment before failing — ASIC may hold money. Human verification only.</summary>
    public const string PaymentRisk = "PaymentRisk";

    /// <summary>Bad data (name not found, invalid ABN) or a declined payment — retrying can't help.</summary>
    public const string Terminal = "Terminal";
}

public static class RenewalErrorClassifier
{
    /// <summary>
    /// A short explanation that is safe to show the customer (wizard confirmation, Business
    /// Portal). Never the raw ErrorMessage, which carries ASIC page text and step names.
    /// Null while the renewal is on track.
    /// </summary>
    public static string? CustomerMessage(RenewalStatus status, string? category, DateTime? nextRetryAt)
    {
        if (status == RenewalStatus.Completed) return null;
        return category switch
        {
            RenewalErrorCategories.NotDueYet => "ASIC isn't accepting this renewal yet — we'll retry automatically.",
            RenewalErrorCategories.AlreadyInProgress => "ASIC shows a renewal already in progress for this name.",
            RenewalErrorCategories.Transient when nextRetryAt != null => "We hit a temporary problem at ASIC and will retry automatically.",
            _ when status == RenewalStatus.Failed => "This renewal needs attention from our team — we'll be in touch.",
            _ => null,
        };
    }

    public static string Classify(string? failedAtStep, string? errorMessage)
    {
        var message = errorMessage ?? "";

        if (failedAtStep == "Complete Payment Action" ||
            message.Contains("Payment processed but completion failed", StringComparison.OrdinalIgnoreCase))
            return RenewalErrorCategories.PaymentRisk;

        if (message.Contains("not due for renewal", StringComparison.OrdinalIgnoreCase))
            return RenewalErrorCategories.NotDueYet;

        if (message.Contains("already in progress", StringComparison.OrdinalIgnoreCase))
            return RenewalErrorCategories.AlreadyInProgress;

        if (message.Contains("not found under ABN", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No business names found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Could not find business name", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("check the ABN", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("declined", StringComparison.OrdinalIgnoreCase))
            return RenewalErrorCategories.Terminal;

        // Exceptions, timeouts, "error occurred while sending the request", failed session
        // init — everything else is treated as retryable rather than silently terminal.
        return RenewalErrorCategories.Transient;
    }
}
