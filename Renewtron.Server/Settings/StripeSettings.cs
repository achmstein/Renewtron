namespace Renewtron.Settings;

public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>Signing secret (whsec_…) of the dispute webhook endpoint in the Stripe dashboard.</summary>
    public string? WebhookSecret { get; set; }
}
