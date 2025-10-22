using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using System.Text.Json;

namespace Renewtron.Services;

public class SettingsService : ISettingsService
{
    private readonly IOptions<SendGridSettings> _sendGridSettings;
    private readonly IOptions<StripeSettings> _stripeSettings;
    private readonly IOptions<TwoCaptchaSettings> _twoCaptchaSettings;
    private readonly IOptions<PricingSettings> _pricingSettings;
    private readonly IOptions<AsicCreditCardSettings> _asicCreditCardSettings;
    private readonly IWebHostEnvironment _environment;

    public SettingsService(
        IOptions<SendGridSettings> sendGridSettings,
        IOptions<StripeSettings> stripeSettings,
        IOptions<TwoCaptchaSettings> twoCaptchaSettings,
        IOptions<PricingSettings> pricingSettings,
        IOptions<AsicCreditCardSettings> asicCreditCardSettings,
        IWebHostEnvironment environment)
    {
        _sendGridSettings = sendGridSettings;
        _stripeSettings = stripeSettings;
        _twoCaptchaSettings = twoCaptchaSettings;
        _pricingSettings = pricingSettings;
        _asicCreditCardSettings = asicCreditCardSettings;
        _environment = environment;
    }

    public Task<SendGridSettings> GetSendGridSettingsAsync()
    {
        return Task.FromResult(_sendGridSettings.Value);
    }

    public Task<StripeSettings> GetStripeSettingsAsync()
    {
        return Task.FromResult(_stripeSettings.Value);
    }

    public Task<TwoCaptchaSettings> GetTwoCaptchaSettingsAsync()
    {
        return Task.FromResult(_twoCaptchaSettings.Value);
    }

    public Task<PricingSettings> GetPricingSettingsAsync()
    {
        return Task.FromResult(_pricingSettings.Value);
    }

    public Task<AsicCreditCardSettings> GetAsicCreditCardSettingsAsync()
    {
        return Task.FromResult(_asicCreditCardSettings.Value);
    }

    public async Task UpdateSendGridSettingsAsync(SendGridSettings settings)
    {
        await UpdateSettingsSectionAsync("SendGrid", settings);
    }

    public async Task UpdateStripeSettingsAsync(StripeSettings settings)
    {
        await UpdateSettingsSectionAsync("Stripe", settings);
    }

    public async Task UpdateTwoCaptchaSettingsAsync(TwoCaptchaSettings settings)
    {
        await UpdateSettingsSectionAsync("TwoCaptcha", settings);
    }

    public async Task UpdatePricingSettingsAsync(PricingSettings settings)
    {
        await UpdateSettingsSectionAsync("Pricing", settings);
    }

    public async Task UpdateAsicCreditCardSettingsAsync(AsicCreditCardSettings settings)
    {
        await UpdateSettingsSectionAsync("AsicCreditCard", settings);
    }

    private async Task UpdateSettingsSectionAsync(string sectionName, object settings)
    {
        var appSettingsPath = Path.Combine(_environment.ContentRootPath, "appsettings.json");

        var json = await File.ReadAllTextAsync(appSettingsPath);
        var jsonDocument = JsonDocument.Parse(json);

        var root = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        if (root != null)
        {
            root[sectionName] = JsonSerializer.Deserialize<Dictionary<string, object>>(
                JsonSerializer.Serialize(settings));

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var updatedJson = JsonSerializer.Serialize(root, options);
            await File.WriteAllTextAsync(appSettingsPath, updatedJson);
        }
    }
}
