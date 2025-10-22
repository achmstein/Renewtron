namespace Renewtron.Abstractions;

public interface IRenewalRetryService
{
    Task<(bool success, string message)> RetryRenewalAsync(Guid renewalRequestId);
}
