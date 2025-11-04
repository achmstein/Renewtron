using Renewtron.Settings;

namespace Renewtron.Abstractions;

public interface ISettingsService
{
    Task<SendGridSettings> GetSendGridSettingsAsync();
    Task<StripeSettings> GetStripeSettingsAsync();
    Task<PricingSettings> GetPricingSettingsAsync();
    Task<AsicCreditCardSettings> GetAsicCreditCardSettingsAsync();
    Task<OntraportSettings> GetOntraportSettingsAsync();

    Task UpdateSendGridSettingsAsync(SendGridSettings settings);
    Task UpdateStripeSettingsAsync(StripeSettings settings);
    Task UpdatePricingSettingsAsync(PricingSettings settings);
    Task UpdateAsicCreditCardSettingsAsync(AsicCreditCardSettings settings);
    Task UpdateOntraportSettingsAsync(OntraportSettings settings);
}
