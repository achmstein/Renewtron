using Hangfire;
using Renewtron.Data;

namespace Renewtron.Abstractions;

public sealed record AsicKeyRequestRunResult(
    bool Skipped,
    string Message,
    int Submitted,
    int Failed,
    int KeysMatched);

public interface IAsicKeyRequestService
{
    /// <summary>
    /// One pass: close off Submitted requests whose key has since arrived in the inbox, then
    /// submit Pending rows (and auto-retryable failures) to ASIC up to the per-run cap.
    /// Hangfire serialises runs so two ticks can't double-submit; a failing run is retried
    /// once and then left for the next tick.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    [AutomaticRetry(Attempts = 1)]
    Task<AsicKeyRequestRunResult> ProcessPendingAsync(CancellationToken ct = default);

    /// <summary>Submits one row to ASIC right now, whatever its status. Null when the id is unknown.</summary>
    Task<AsicKeyRequest?> SubmitAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Creates (or re-opens) the request for a sale's business name. With <paramref name="submitNow"/>
    /// it is sent to ASIC inline; otherwise the next run picks it up.
    /// </summary>
    Task<AsicKeyRequest> QueueForSaleAsync(Guid saleId, bool submitNow, CancellationToken ct = default);
}
