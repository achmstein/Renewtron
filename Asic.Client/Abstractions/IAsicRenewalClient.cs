using Asic.Client.Models;

namespace Asic.Client.Abstractions;
public interface IAsicRenewalClient
{
    Task<BusinessNamesResult> SearchByAbnAsync(string abn);

    Task<RenewalResult> RenewBusinessNameAsync(
        string abn,
        string accountNumber,
        int renewalYears,
        string email,
        CreditCardDetails cardDetails);
}