namespace Renewtron.Data;

public enum OntraportSaleStatus
{
    Synced = 0,
    WaitingForRenewalWindow = 1,
    RenewalQueued = 2,
    RenewalCompleted = 3,
    RenewalFailed = 4,
    NotDueForRenewal = 5,
    IneligibleForRenewal = 6,
    // ASIC returned "not due for renewal yet" — retry on the next process run.
    AsicNotYetDue = 7,
    // ASIC reports an existing renewal session is already in progress for this ABN.
    RenewalInProgress = 8
}

public static class OntraportSaleStatusClassifier
{
    /// <summary>
    /// Maps an ASIC/renewal error message to a status. "not due for renewal" and
    /// "already in progress" are transient — the next process run retries them.
    /// Anything else stays as RenewalFailed.
    /// </summary>
    /// <summary>
    /// A sale's status after its renewal failed, from the renewal's ErrorCategory (see
    /// RenewalErrorClassifier.Classify). That category already puts "ASIC may hold money" and
    /// "card declined" ahead of "not due" / "in progress", so a run that reached ASIC's payment
    /// step is never put back in the queue (which could pay ASIC twice).
    /// </summary>
    public static OntraportSaleStatus FromRenewalCategory(string? category) => category switch
    {
        RenewalErrorCategories.NotDueYet => OntraportSaleStatus.AsicNotYetDue,
        RenewalErrorCategories.AlreadyInProgress => OntraportSaleStatus.RenewalInProgress,
        _ => OntraportSaleStatus.RenewalFailed,
    };

    /// <summary>For failures before a renewal exists (the ASIC name search in the sale processor).</summary>
    public static OntraportSaleStatus ClassifyError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return OntraportSaleStatus.RenewalFailed;

        if (errorMessage.Contains("not due for renewal", StringComparison.OrdinalIgnoreCase))
            return OntraportSaleStatus.AsicNotYetDue;

        if (errorMessage.Contains("already in progress", StringComparison.OrdinalIgnoreCase))
            return OntraportSaleStatus.RenewalInProgress;

        return OntraportSaleStatus.RenewalFailed;
    }
}
