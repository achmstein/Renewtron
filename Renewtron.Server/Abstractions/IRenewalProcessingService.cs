namespace Renewtron.Abstractions;

public interface IRenewalProcessingService
{
    /// <summary>
    /// Processes the ASIC renewal for a given renewal request in the background
    /// </summary>
    /// <param name="renewalRequestId">The ID of the renewal request to process</param>
    Task ProcessRenewalAsync(Guid renewalRequestId);
}
