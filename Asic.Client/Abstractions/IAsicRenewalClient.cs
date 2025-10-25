using Asic.Client.Models;

namespace Asic.Client.Abstractions;
public interface IAsicRenewalClient
{
    Task<BusinessNamesSearchResult> SearchAsync(string abn);

    Task<RenewalResult> RenewBusinessNameAsync(
        string abn,
        string businessName,
        int renewalYears,
        string email,
        CreditCardDetails cardDetails);
}