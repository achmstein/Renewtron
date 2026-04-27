using Renewtron.Settings;

namespace Renewtron.Abstractions;

public interface ISettingsService
{
    Task<SendGridSettings> GetSendGridSettingsAsync();
    Task<StripeSettings> GetStripeSettingsAsync();
    Task<PricingSettings> GetPricingSettingsAsync();
    Task<AsicSettings> GetAsicSettingsAsync();
    Task<OntraportSettings> GetOntraportSettingsAsync();

    Task UpdateSendGridSettingsAsync(SendGridSettings settings);
    Task UpdateStripeSettingsAsync(StripeSettings settings);
    Task UpdatePricingSettingsAsync(PricingSettings settings);
    Task UpdateAsicSettingsAsync(AsicSettings settings);
    Task UpdateOntraportSettingsAsync(OntraportSettings settings);
}
