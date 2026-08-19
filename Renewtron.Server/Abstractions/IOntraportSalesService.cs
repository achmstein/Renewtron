using Renewtron.Data;

namespace Renewtron.Abstractions;

public interface IOntraportSalesService
{
    Task<List<OntraportSale>> SyncSalesAsync();
    Task ProcessEligibleRenewalsAsync();

    /// <summary>
    /// Writes the outcome of a renewal attempt back onto the Ontraport contact: the renewal
    /// status field, and — on success only — the rolled-forward renewal due date.
    /// Returns true only when every Ontraport write was acknowledged; callers use this to
    /// keep the outbox row pending for retry.
    /// </summary>
    Task<bool> SyncRenewalOutcomeAsync(string contactId, OntraportRenewalOutcome outcome, DateTime? newRenewalDueDate = null);

    /// <summary>
    /// Retries pending OntraportSyncOutbox rows (renewal outcomes whose write-back to
    /// Ontraport failed earlier). Returns how many were sent this run.
    /// </summary>
    Task<int> ProcessSyncOutboxAsync();
}
