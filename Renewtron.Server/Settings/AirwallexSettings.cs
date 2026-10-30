namespace Renewtron.Settings;

/// <summary>
/// Airwallex, used to pay ASIC's BPAY bills (Admin → BPAY to pay). Bound from "Airwallex";
/// edited on the admin Settings page. Keys come from Airwallex → Settings → Developer → API keys.
/// </summary>
public class AirwallexSettings
{
    public string? ClientId { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>True sends everything to Airwallex's sandbox, where no money moves.</summary>
    public bool UseSandbox { get; set; } = true;

    public string BaseUrl => UseSandbox ? "https://api.sandbox.airwallex.com" : "https://api.airwallex.com";

    public bool Ready => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ApiKey);
}
