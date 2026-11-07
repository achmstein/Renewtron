namespace Renewtron.Settings;

public class SendGridSettings
{
    public string ApiKey { get; set; }
    public string FromEmail { get; set; }
    public string FromName { get; set; }

    /// <summary>
    /// SendGrid → Mail Settings → Event Webhook → "Signed Event Webhook" verification key (base64).
    /// Events are only accepted when they're signed with it; blank = the webhook is off.
    /// </summary>
    public string? EventWebhookPublicKey { get; set; }
}
