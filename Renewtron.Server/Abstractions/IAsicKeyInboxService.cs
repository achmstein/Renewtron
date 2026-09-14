using Hangfire;
using Renewtron.Data;

namespace Renewtron.Abstractions;

public sealed record AsicKeyScanResult(
    bool Skipped,
    string Message,
    int Discovered,
    int Processed,
    int Completed,
    int Failed);

public interface IAsicKeyInboxService
{
    /// <summary>
    /// One full pass: pull new "Notification request" emails from the inbox, then work
    /// every Pending row through link → PDF → key → Ontraport. Hangfire serialises runs
    /// (see the attribute) so two recurring ticks can't overlap. A failing scan is retried
    /// twice, then left for the next cron tick — a bad app password shouldn't pile up
    /// ten back-off retries every 15 minutes.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 2)]
    Task<AsicKeyScanResult> ScanAsync(CancellationToken ct = default);

    /// <summary>Re-runs the pipeline for one row (any status), returning the updated row.</summary>
    Task<AsicKeyNotification?> ProcessAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Writes an already-extracted key onto a contact the operator picked by hand —
    /// the escape hatch when automatic business-name matching finds nothing.
    /// </summary>
    Task<AsicKeyNotification?> ApplyToContactAsync(Guid id, string contactId, CancellationToken ct = default);
}
