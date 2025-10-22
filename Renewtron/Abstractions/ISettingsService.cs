using Renewtron.Settings;

namespace Renewtron.Abstractions;

public interface ISettingsService
{
    Task<SendGridSettings> GetSendGridSettingsAsync();
    Task<StripeSettings> GetStripeSettingsAsync();
    Task<TwoCaptchaSettings> GetTwoCaptchaSettingsAsync();
    Task<PricingSettings> GetPricingSettingsAsync();
    Task<AsicCreditCardSettings> GetAsicCreditCardSettingsAsync();

    Task UpdateSendGridSettingsAsync(SendGridSettings settings);
    Task UpdateStripeSettingsAsync(StripeSettings settings);
    Task UpdateTwoCaptchaSettingsAsync(TwoCaptchaSettings settings);
    Task UpdatePricingSettingsAsync(PricingSettings settings);
    Task UpdateAsicCreditCardSettingsAsync(AsicCreditCardSettings settings);
}
