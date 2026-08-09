using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Renewtron.Services;

public class SettingsService : ISettingsService
{
    // IOptionsMonitor (not IOptions) so the GET endpoint sees writes made by
    // UpdateSettingsSectionAsync — IOptions caches the singleton value captured
    // at startup and never reflects reloadOnChange refreshes.
    private readonly IOptionsMonitor<SendGridSettings> _sendGridSettings;
    private readonly IOptionsMonitor<StripeSettings> _stripeSettings;
    private readonly IOptionsMonitor<PricingSettings> _pricingSettings;
    private readonly IOptionsMonitor<AsicSettings> _asicSettings;
    private readonly IOptionsMonitor<OntraportSettings> _ontraportSettings;
    private readonly IOptionsMonitor<AtoAgentSettings> _atoAgentSettings;
    private readonly IOptionsMonitor<WinBackSettings> _winBackSettings;
    private readonly IOptionsMonitor<TrackingSettings> _trackingSettings;
    private readonly string _overridesPath;

    public SettingsService(
        IOptionsMonitor<SendGridSettings> sendGridSettings,
        IOptionsMonitor<StripeSettings> stripeSettings,
        IOptionsMonitor<PricingSettings> pricingSettings,
        IOptionsMonitor<AsicSettings> asicSettings,
        IOptionsMonitor<OntraportSettings> ontraportSettings,
        IOptionsMonitor<AtoAgentSettings> atoAgentSettings,
        IOptionsMonitor<WinBackSettings> winBackSettings,
        IOptionsMonitor<TrackingSettings> trackingSettings,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _sendGridSettings = sendGridSettings;
        _stripeSettings = stripeSettings;
        _pricingSettings = pricingSettings;
        _asicSettings = asicSettings;
        _ontraportSettings = ontraportSettings;
        _atoAgentSettings = atoAgentSettings;
        _winBackSettings = winBackSettings;
        _trackingSettings = trackingSettings;

        // Match Program.cs: writable overrides file lives outside the image.
        _overridesPath = configuration["Storage:OverridesPath"]
            ?? Path.Combine(environment.ContentRootPath, "settings.overrides.json");
    }

    public Task<SendGridSettings> GetSendGridSettingsAsync()
    {
        return Task.FromResult(_sendGridSettings.CurrentValue);
    }

    public Task<StripeSettings> GetStripeSettingsAsync()
    {
        return Task.FromResult(_stripeSettings.CurrentValue);
    }

    public Task<PricingSettings> GetPricingSettingsAsync()
    {
        return Task.FromResult(_pricingSettings.CurrentValue);
    }

    public Task<AsicSettings> GetAsicSettingsAsync()
    {
        return Task.FromResult(_asicSettings.CurrentValue);
    }

    public Task<OntraportSettings> GetOntraportSettingsAsync()
    {
        return Task.FromResult(_ontraportSettings.CurrentValue);
    }

    public Task<AtoAgentSettings> GetAtoAgentSettingsAsync()
    {
        return Task.FromResult(_atoAgentSettings.CurrentValue);
    }

    public Task<WinBackSettings> GetWinBackSettingsAsync()
    {
        return Task.FromResult(_winBackSettings.CurrentValue);
    }

    public Task<TrackingSettings> GetTrackingSettingsAsync()
    {
        return Task.FromResult(_trackingSettings.CurrentValue);
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

    public async Task UpdateWinBackSettingsAsync(WinBackSettings settings)
    {
        await UpdateSettingsSectionAsync("WinBack", settings);
    }

    public async Task UpdateTrackingSettingsAsync(TrackingSettings settings)
    {
        await UpdateSettingsSectionAsync("Tracking", settings);
    }

    private async Task UpdateSettingsSectionAsync(string sectionName, object settings)
    {
        // Load existing overrides if present, else start empty. We never touch
        // appsettings.json — it ships inside the read-only container image.
        JsonObject root;
        if (File.Exists(_overridesPath))
        {
            var existing = await File.ReadAllTextAsync(_overridesPath);
            root = (JsonNode.Parse(existing) as JsonObject) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
            var dir = Path.GetDirectoryName(_overridesPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        var settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        root[sectionName] = JsonNode.Parse(settingsJson);

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        await File.WriteAllTextAsync(_overridesPath, JsonSerializer.Serialize(root, writeOptions));
    }
}
