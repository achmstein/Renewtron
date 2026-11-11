using Hangfire;

namespace Renewtron.Abstractions;

public interface IRenewalProcessingService
{
    /// <summary>
    /// Processes the ASIC renewal for a given renewal request in the background.
    /// Hangfire resolves filter attributes from this interface method (jobs are enqueued
    /// against the interface), so they must live here — on the implementation they are inert.
    /// AutomaticRetry is disabled because the method handles its own categorized,
    /// backoff-scheduled retries and never throws.
    /// </summary>
    /// <param name="renewalRequestId">The ID of the renewal request to process</param>
    [Queue("asic")]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    Task ProcessRenewalAsync(Guid renewalRequestId);
}
