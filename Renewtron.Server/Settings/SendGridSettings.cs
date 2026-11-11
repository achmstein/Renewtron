namespace Renewtron.Settings;

public class SendGridSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// SendGrid → Mail Settings → Event Webhook → "Signed Event Webhook" verification key (base64).
    /// Events are only accepted when they're signed with it; blank = the webhook is off.
    /// </summary>
    public string? EventWebhookPublicKey { get; set; }
}
