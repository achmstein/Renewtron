namespace Renewtron.Abstractions;

public interface IRenewalReconciliationService
{
    /// <summary>
    /// Finds renewals whose DB state has diverged from the Hangfire queue — Pending rows
    /// with no live job, and Processing rows that stalled — and either re-queues them or
    /// flags them for operator verification. With <paramref name="dryRun"/> nothing is
    /// changed; the report shows what would happen.
    /// </summary>
    Task<RenewalReconciliationReport> ReconcileAsync(bool dryRun, int maxRequeue);
}

public sealed record RenewalReconciliationItem(
    Guid RenewalId,
    string? BusinessName,
    string? Abn,
    decimal Amount,
    string Status,
    int AgeHours,
    string Action);

public sealed record RenewalReconciliationReport(
    bool DryRun,
    int Scanned,
    int SkippedLiveJob,
    int Requeued,
    int NeedsVerification,
    int MarkedStale,
    int RequeueCapped,
    IReadOnlyList<RenewalReconciliationItem> Items);
