using Hangfire;
using Renewtron.Data;

namespace Renewtron.Abstractions;

public sealed record AsicKeyRequestRunResult(
    bool Skipped,
    string Message,
    int KeysMatched,
    int Requeued);

public interface IAsicKeyRequestService
{
    /// <summary>
    /// Housekeeping pass: close off Submitted requests whose key has since arrived in the
    /// inbox, and put back into the queue any request a person took but never closed.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 1)]
    Task<AsicKeyRequestRunResult> ProcessPendingAsync(CancellationToken ct = default);

    /// <summary>Creates (or re-opens) the request for a sale's business name, for a person to send.</summary>
    Task<AsicKeyRequest> QueueForSaleAsync(Guid saleId, CancellationToken ct = default);
}
