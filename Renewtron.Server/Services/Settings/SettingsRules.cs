using Asic.Integrations.Portal;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// What each Settings section must look like before it is saved: secrets un-masked, text
/// trimmed, defaults filled in, and bad values refused. Pure functions, one per section, so the
/// rules are tested without HTTP. Each Prepare* returns an error for the admin, or null.
/// </summary>
/// <remarks>
/// Secrets are write-only: the page gets a masked placeholder (last 4 kept for recognition),
/// and saving that placeholder back keeps the stored value. The ASIC CVC never leaves the server.
/// </remarks>
public static class SettingsRules
{
    public const string MaskPrefix = "••••";

    public static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? "" : MaskPrefix + (value.Length > 4 ? value[^4..] : "");

    /// <summary>The stored value when the page sent the placeholder back, else what it sent.</summary>
    public static string Unmask(string? incoming, string? current) =>
        incoming != null && incoming.StartsWith(MaskPrefix, StringComparison.Ordinal) ? current ?? "" : incoming ?? "";

    public static string? PrepareSendGrid(SendGridSettings body, SendGridSettings current)
    {
        body.ApiKey = Unmask(body.ApiKey, current.ApiKey);
        // A page that doesn't send the webhook key keeps the stored one.
        body.EventWebhookPublicKey = body.EventWebhookPublicKey is null ? current.EventWebhookPublicKey : body.EventWebhookPublicKey.Trim();
        return null;
    }

    public static string? PrepareStripe(StripeSettings body, StripeSettings current)
    {
        body.SecretKey = Unmask(body.SecretKey, current.SecretKey);
        body.WebhookSecret = Unmask(body.WebhookSecret, current.WebhookSecret).Trim();
        return null;
    }

    public static string? PrepareAsic(AsicSettings body, AsicSettings current)
    {
        if (body.OneYearFee < 0 || body.ThreeYearFee < 0) return "ASIC fees can't be negative.";
        body.CardNumber = Unmask(body.CardNumber, current.CardNumber);
        if (string.IsNullOrEmpty(body.Cvc)) body.Cvc = current.Cvc;
        body.PaymentMethod = AsicPaymentMethods.IsBpay(body.PaymentMethod) ? AsicPaymentMethods.Bpay : AsicPaymentMethods.Card;
        body.BpayAfterCardFailures = Math.Clamp(body.BpayAfterCardFailures, 0, AsicSettings.MaxBpayAfterCardFailures);
        return null;
    }

    public static string? PrepareOntraport(OntraportSettings body, OntraportSettings current)
    {
        body.ApiAppId = Unmask(body.ApiAppId, current.ApiAppId);
        body.ApiKey = Unmask(body.ApiKey, current.ApiKey);
        return null;
    }

    /// <summary>Shared by save and test so both see the same effective values.</summary>
    public static string? PrepareAsicKeyInbox(AsicKeyInboxSettings body, AsicKeyInboxSettings current)
    {
        body.Password = Unmask(body.Password, current.Password);
        body.ImapHost = string.IsNullOrWhiteSpace(body.ImapHost) ? "imap.gmail.com" : body.ImapHost.Trim();
        body.Username = (body.Username ?? "").Trim();
        if (body.ImapPort <= 0) body.ImapPort = 993;
        if (body.LookbackDays <= 0) body.LookbackDays = 30;
        if (string.IsNullOrWhiteSpace(body.Folder)) body.Folder = "INBOX";
        if (string.IsNullOrWhiteSpace(body.SubjectFilter)) body.SubjectFilter = "Notification request";
        if (string.IsNullOrWhiteSpace(body.AsicKeyPattern)) body.AsicKeyPattern = AsicKeyInboxSettings.DefaultAsicKeyPattern;
        return null;
    }

    public static string? PrepareAsicKeyRequest(AsicKeyRequestSettings body)
    {
        body.RequestEmail = (body.RequestEmail ?? "").Trim();
        body.DefaultPhonePrefix = (body.DefaultPhonePrefix ?? "").Trim();
        body.DefaultPhoneNumber = (body.DefaultPhoneNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body.MessageTemplate)) body.MessageTemplate = AsicKeyRequestSettings.DefaultMessageTemplate;
        return null;
    }

    /// <summary>The page's ASIC Connect login form, before it becomes <see cref="AsicPortalSettings"/>.</summary>
    public record AsicPortalForm(bool Enabled, string? Username, string? Password, string? BusinessEmail,
        string? KeepRepresentatives, string? DeclareAs, string? OntraportLinkFieldId, string? WrongKeyCampaignId);

    public static (AsicPortalSettings Settings, string? Error) PrepareAsicPortal(AsicPortalForm body, AsicPortalSettings current)
    {
        var saved = new AsicPortalSettings
        {
            Enabled = body.Enabled,
            Username = (body.Username ?? "").Trim(),
            Password = Unmask(body.Password, current.Password),
            BusinessEmail = (body.BusinessEmail ?? "").Trim(),
            KeepRepresentatives = (body.KeepRepresentatives ?? "").Trim(),
            DeclareAs = Enum.TryParse<AsicDeclareAs>(body.DeclareAs, true, out var d) ? d : AsicDeclareAs.Holder,
            OntraportLinkFieldId = (body.OntraportLinkFieldId ?? "").Trim(),
            WrongKeyCampaignId = (body.WrongKeyCampaignId ?? "").Trim(),
        };
        return (saved, string.IsNullOrWhiteSpace(saved.BusinessEmail) ? "The business email can't be blank." : null);
    }

    public static string? PrepareRegistration(RegistrationSettings body)
    {
        if (body.OneYearPrice < 0 || body.ThreeYearPrice < 0) return "Prices can't be negative.";
        if (body.Enabled && (body.PriceFor(1) is null || body.PriceFor(3) is null))
            return "Set both prices before switching the register page on.";
        return null;
    }

    public static string? PrepareAirwallex(AirwallexSettings body, AirwallexSettings current)
    {
        body.ClientId = (body.ClientId ?? "").Trim();
        body.AutoPayDailyLimit = Math.Max(0, body.AutoPayDailyLimit);
        body.ApiKey = Unmask(body.ApiKey, current.ApiKey).Trim();
        return null;
    }

    public static string? PrepareAsicRegistry(AsicRegistrySection body, Asic.Integrations.Registry.AsicRegistryOptions current)
    {
        if (body.MaxLookupsPerRun < 0 || body.RecheckAfterDays < 0 || body.RecheckWhenDueWithinDays < 0 || body.ConsecutiveFailureLimit < 0)
            return "Numbers can't be negative.";
        var proxyUrl = (body.ProxyUrl ?? "").Trim();
        if (proxyUrl.Length > 0 && !Uri.TryCreate(proxyUrl, UriKind.Absolute, out _))
            return "Proxy URL must look like http://host:port or socks5://host:port.";

        body.TwoCaptchaApiKey = Unmask(body.TwoCaptchaApiKey, current.TwoCaptchaApiKey).Trim();
        body.ProxyPassword = Unmask(body.ProxyPassword, current.ProxyPassword);
        body.ProxyUrl = proxyUrl;
        body.ProxyUsername = (body.ProxyUsername ?? "").Trim();
        return null;
    }

    public static string? PrepareTestMode(TestModeSettings body, TestModeSettings current)
    {
        body.StripeSecretKey = Unmask(body.StripeSecretKey, current.StripeSecretKey).Trim();
        body.StripePublishableKey = (body.StripePublishableKey ?? "").Trim();
        // A live key here would charge test customers for real: refuse anything but test keys.
        if (body.StripeSecretKey.Length > 0 && !body.StripeSecretKey.StartsWith("sk_test_", StringComparison.Ordinal))
            return "The Stripe secret key must be a test key (sk_test_…).";
        if (body.StripePublishableKey.Length > 0 && !body.StripePublishableKey.StartsWith("pk_test_", StringComparison.Ordinal))
            return "The Stripe publishable key must be a test key (pk_test_…).";
        body.TestEmails = string.Join("\n", body.Emails());
        return null;
    }
}
