using Renewtron.Settings;

namespace Renewtron.Abstractions;

public interface ISettingsService
{
    Task<SendGridSettings> GetSendGridSettingsAsync();
    Task<AirwallexSettings> GetAirwallexSettingsAsync();
    Task<PricingSettings> GetPricingSettingsAsync();
    Task<AsicSettings> GetAsicSettingsAsync();
    Task<OntraportSettings> GetOntraportSettingsAsync();

    Task UpdateSendGridSettingsAsync(SendGridSettings settings);
    Task UpdateAirwallexSettingsAsync(AirwallexSettings settings);
    Task UpdatePricingSettingsAsync(PricingSettings settings);
    Task UpdateAsicSettingsAsync(AsicSettings settings);
    Task UpdateOntraportSettingsAsync(OntraportSettings settings);
}
