using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        // Read the existing JSON
        var json = await File.ReadAllTextAsync(appSettingsPath);

        // Parse as JsonNode for manipulation
        var jsonNode = JsonNode.Parse(json);

        if (jsonNode is JsonObject root)
        {
            // Serialize the settings object to JSON
            var settingsJson = JsonSerializer.Serialize(settings);

            // Parse the settings JSON and add to root
            var settingsNode = JsonNode.Parse(settingsJson);
            root[sectionName] = settingsNode;

            // Write back with proper formatting
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var updatedJson = root.ToJsonString(options);
            await File.WriteAllTextAsync(appSettingsPath, updatedJson);
        }
    }
}
