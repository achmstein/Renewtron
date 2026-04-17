using Renewtron.Data;

namespace Renewtron.Abstractions;

public interface IOntraportSalesService
{
    Task<List<OntraportSale>> SyncSalesAsync();
    Task ProcessEligibleRenewalsAsync();
    Task UpdateOntraportContactStatusAsync(string contactId, bool success, string? transactionReference = null);
}
