namespace Renewtron.Abstractions;

public interface IAtoOnboardingService
{
    Task EnqueueAsync(Guid renewalRequestId);
    Task PollAsync(Guid renewalRequestId, string jobId, int attempt);
}
