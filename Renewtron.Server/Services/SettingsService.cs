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
    private readonly IOptionsMonitor<WinBackSettings> _winBackSettings;
    private readonly string _overridesPath;

    public SettingsService(
        IOptions<SendGridSettings> sendGridSettings,
        IOptions<StripeSettings> stripeSettings,
        IOptions<PricingSettings> pricingSettings,
        IOptions<AsicSettings> asicSettings,
        IOptions<OntraportSettings> ontraportSettings,
        IOptionsMonitor<AtoAgentSettings> atoAgentSettings,
        IOptionsMonitor<WinBackSettings> winBackSettings,
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

        // Match Program.cs: writable overrides file lives outside the image.
        _overridesPath = configuration["Storage:OverridesPath"]
            ?? Path.Combine(environment.ContentRootPath, "settings.overrides.json");
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

    public Task<WinBackSettings> GetWinBackSettingsAsync()
    {
        return Task.FromResult(_winBackSettings.CurrentValue);
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
