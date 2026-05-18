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
