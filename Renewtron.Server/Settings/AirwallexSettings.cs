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

    /// <summary>
    /// Pay ASIC's BPAY bills (renewals and new names) on their own, every 15 minutes, instead of
    /// waiting for someone on "BPAY to pay". ASIC's set fee only; a bill that fails 3 times waits for a person.
    /// </summary>
    public bool AutoPayBpay { get; set; } = true;

    /// <summary>The most auto-pay sends in one day (AEST), counting manual payments; the rest wait for a person.</summary>
    public decimal AutoPayDailyLimit { get; set; } = 3000m;

    public string BaseUrl => UseSandbox ? "https://api.sandbox.airwallex.com" : "https://api.airwallex.com";

    public bool Ready => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ApiKey);
}
