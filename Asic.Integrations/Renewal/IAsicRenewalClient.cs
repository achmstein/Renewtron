using Asic.Integrations.Payment;


namespace Asic.Integrations.Renewal;
public interface IAsicRenewalClient
{
    Task<BusinessNamesResult> SearchByAbnAsync(string abn);

    Task<RenewalResult> RenewBusinessNameAsync(
        string abn,
        string accountNumber,
        int renewalYears,
        string email,
        CreditCardDetails cardDetails,
        bool payLater = false);
}