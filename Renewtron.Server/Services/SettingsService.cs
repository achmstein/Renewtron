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
    private readonly IOptions<PricingSettings> _pricingSettings;
    private readonly IOptions<AsicSettings> _asicSettings;
    private readonly IOptions<OntraportSettings> _ontraportSettings;
    private readonly IOptionsMonitor<AtoAgentSettings> _atoAgentSettings;
    private readonly IWebHostEnvironment _environment;

    public SettingsService(
        IOptions<SendGridSettings> sendGridSettings,
        IOptions<StripeSettings> stripeSettings,
        IOptions<PricingSettings> pricingSettings,
        IOptions<AsicSettings> asicSettings,
        IOptions<OntraportSettings> ontraportSettings,
        IOptionsMonitor<AtoAgentSettings> atoAgentSettings,
        IWebHostEnvironment environment)
    {
        _sendGridSettings = sendGridSettings;
        _stripeSettings = stripeSettings;
        _pricingSettings = pricingSettings;
        _asicSettings = asicSettings;
        _ontraportSettings = ontraportSettings;
        _atoAgentSettings = atoAgentSettings;
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

    public Task<PricingSettings> GetPricingSettingsAsync()
    {
        return Task.FromResult(_pricingSettings.Value);
    }

    public Task<AsicSettings> GetAsicSettingsAsync()
    {
        return Task.FromResult(_asicSettings.Value);
    }

    public Task<OntraportSettings> GetOntraportSettingsAsync()
    {
        return Task.FromResult(_ontraportSettings.Value);
    }

    public Task<AtoAgentSettings> GetAtoAgentSettingsAsync()
    {
        return Task.FromResult(_atoAgentSettings.CurrentValue);
    }

    public async Task UpdateSendGridSettingsAsync(SendGridSettings settings)
    {
        await UpdateSettingsSectionAsync("SendGrid", settings);
    }

    public async Task UpdateStripeSettingsAsync(StripeSettings settings)
    {
        await UpdateSettingsSectionAsync("Stripe", settings);
    }

    public async Task UpdatePricingSettingsAsync(PricingSettings settings)
    {
        await UpdateSettingsSectionAsync("Pricing", settings);
    }

    public async Task UpdateAsicSettingsAsync(AsicSettings settings)
    {
        await UpdateSettingsSectionAsync("Asic", settings);
    }

    public async Task UpdateOntraportSettingsAsync(OntraportSettings settings)
    {
        await UpdateSettingsSectionAsync("Ontraport", settings);
    }

    public async Task UpdateAtoAgentSettingsAsync(AtoAgentSettings settings)
    {
        await UpdateSettingsSectionAsync("AtoAgent", settings);
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
            // Serialize the settings object to JSON with proper options
            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = false // We'll handle indentation at the root level
            };

            var settingsJson = JsonSerializer.Serialize(settings, serializerOptions);

            // Parse the settings JSON and add to root
            var settingsNode = JsonNode.Parse(settingsJson);
            root[sectionName] = settingsNode;

            // Write back with proper formatting using JsonSerializer
            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var updatedJson = JsonSerializer.Serialize(root, writeOptions);
            await File.WriteAllTextAsync(appSettingsPath, updatedJson);
        }
    }
}
