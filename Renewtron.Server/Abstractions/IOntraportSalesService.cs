using Renewtron.Data;

namespace Renewtron.Abstractions;

public interface IOntraportSalesService
{
    Task<List<OntraportSale>> SyncSalesAsync();
    Task ProcessEligibleRenewalsAsync();

    /// <summary>
    /// Writes the outcome of a renewal attempt back onto the Ontraport contact: the renewal
    /// status field, and — on success only — the rolled-forward renewal due date.
    /// </summary>
    Task SyncRenewalOutcomeAsync(string contactId, OntraportRenewalOutcome outcome, DateTime? newRenewalDueDate = null);
}
