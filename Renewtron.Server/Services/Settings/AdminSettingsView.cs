using Asic.Integrations.Registry;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// Everything the admin Settings page loads, in one payload. Secrets go out masked
/// (<see cref="SettingsRules.Mask"/>) and the ASIC CVC never leaves the server.
/// </summary>
public sealed class AdminSettingsView(
    ISettingsService settings,
    IOptionsMonitor<TestModeSettings> testModeOptions,
    IOptionsMonitor<AirwallexSettings> airwallexOptions,
    IOptionsMonitor<AsicPortalSettings> asicPortalOptions,
    IOptionsMonitor<RegistrationSettings> registrationOptions,
    IOptionsMonitor<AsicRegistrySettings> registrySettings,
    IOptionsMonitor<AsicRegistryOptions> registryOptions)
{
    public async Task<object> LoadAsync()
    {
        var registry = registrySettings.CurrentValue;
        var registryConnection = registryOptions.CurrentValue;
        var registration = registrationOptions.CurrentValue;
        var testMode = testModeOptions.CurrentValue;
        var airwallex = airwallexOptions.CurrentValue;
        var asicPortal = asicPortalOptions.CurrentValue;
        var sendGrid = await settings.GetSendGridSettingsAsync();
        var stripe = await settings.GetStripeSettingsAsync();
        var asic = await settings.GetAsicSettingsAsync();
        var ontraport = await settings.GetOntraportSettingsAsync();
        var asicKeyInbox = await settings.GetAsicKeyInboxSettingsAsync();
        var asicKeyRequest = await settings.GetAsicKeyRequestSettingsAsync();

        return new
        {
            sendGrid = new { apiKey = SettingsRules.Mask(sendGrid.ApiKey), fromEmail = sendGrid.FromEmail, fromName = sendGrid.FromName, eventWebhookPublicKey = sendGrid.EventWebhookPublicKey ?? "" },
            stripe = new { secretKey = SettingsRules.Mask(stripe.SecretKey), publishableKey = stripe.PublishableKey, webhookSecret = SettingsRules.Mask(stripe.WebhookSecret) },
            pricing = await settings.GetPricingSettingsAsync(),
            asic = new
            {
                forceFallback = asic.ForceFallback,
                allowPayEarly = asic.AllowPayEarly,
                email = asic.Email,
                cardNumber = SettingsRules.Mask(asic.CardNumber),
                cardholderName = asic.CardholderName,
                expiryMonth = asic.ExpiryMonth,
                expiryYear = asic.ExpiryYear,
                // Deliberately never returned. Saving with an empty value keeps the stored one;
                // hasCvc lets the UI show the section as complete without seeing the value.
                cvc = "",
                hasCvc = !string.IsNullOrEmpty(asic.Cvc),
                paymentMethod = AsicPaymentMethods.IsBpay(asic.PaymentMethod) ? AsicPaymentMethods.Bpay : AsicPaymentMethods.Card,
                bpayAfterCardFailures = asic.BpayAfterCardFailures,
                // In this section: the ASIC card loads and saves them. (They once sat under
                // asicKeyInbox, so the card showed them empty and every save wrote 0.)
                oneYearFee = asic.OneYearFee,
                threeYearFee = asic.ThreeYearFee,
            },
            ontraport = new
            {
                apiAppId = SettingsRules.Mask(ontraport.ApiAppId),
                apiKey = SettingsRules.Mask(ontraport.ApiKey),
                conversationId = ontraport.ConversationId,
                pushWizardContacts = ontraport.PushWizardContacts,
                wizardLeadTagId = ontraport.WizardLeadTagId,
                wizardPaidTagId = ontraport.WizardPaidTagId,
                defaultPollingTimeoutSeconds = ontraport.DefaultPollingTimeoutSeconds,
            },
            winBack = await settings.GetWinBackSettingsAsync(),
            tracking = await settings.GetTrackingSettingsAsync(),
            asicKeyInbox = new
            {
                enabled = asicKeyInbox.Enabled,
                imapHost = asicKeyInbox.ImapHost,
                imapPort = asicKeyInbox.ImapPort,
                username = asicKeyInbox.Username,
                password = SettingsRules.Mask(asicKeyInbox.Password),
                folder = asicKeyInbox.Folder,
                subjectFilter = asicKeyInbox.SubjectFilter,
                lookbackDays = asicKeyInbox.LookbackDays,
                ontraportFieldId = asicKeyInbox.OntraportFieldId,
                asicKeyPattern = asicKeyInbox.AsicKeyPattern,
                // Saved overrides pin the pattern, so a newer code default needs a way back in.
                defaultAsicKeyPattern = AsicKeyInboxSettings.DefaultAsicKeyPattern,
            },
            asicPortal = new
            {
                enabled = asicPortal.Enabled,
                username = asicPortal.Username,
                password = SettingsRules.Mask(asicPortal.Password),
                businessEmail = asicPortal.BusinessEmail,
                keepRepresentatives = asicPortal.KeepRepresentatives,
                declareAs = asicPortal.DeclareAs.ToString(),
                ontraportLinkFieldId = asicPortal.OntraportLinkFieldId,
                wrongKeyCampaignId = asicPortal.WrongKeyCampaignId,
            },
            registration = new
            {
                enabled = registration.Enabled,
                lodge = registration.Lodge,
                oneYearPrice = registration.OneYearPrice,
                threeYearPrice = registration.ThreeYearPrice,
                allowManualDecision = registration.AllowManualDecision,
            },
            airwallex = new
            {
                clientId = airwallex.ClientId ?? "",
                apiKey = SettingsRules.Mask(airwallex.ApiKey),
                useSandbox = airwallex.UseSandbox,
                autoPayBpay = airwallex.AutoPayBpay,
                autoPayDailyLimit = airwallex.AutoPayDailyLimit,
            },
            asicKeyRequest = new
            {
                enabled = asicKeyRequest.Enabled,
                autoRequestOnSync = asicKeyRequest.AutoRequestOnSync,
                requestEmail = asicKeyRequest.RequestEmail,
                defaultPhonePrefix = asicKeyRequest.DefaultPhonePrefix,
                defaultPhoneNumber = asicKeyRequest.DefaultPhoneNumber,
                messageTemplate = asicKeyRequest.MessageTemplate,
                defaultMessageTemplate = AsicKeyRequestSettings.DefaultMessageTemplate,
            },
            asicRegistry = new
            {
                enabled = registry.Enabled,
                maxLookupsPerRun = registry.MaxLookupsPerRun,
                recheckAfterDays = registry.RecheckAfterDays,
                recheckWhenDueWithinDays = registry.RecheckWhenDueWithinDays,
                consecutiveFailureLimit = registry.ConsecutiveFailureLimit,
                twoCaptchaApiKey = SettingsRules.Mask(registryConnection.TwoCaptchaApiKey),
                proxyUrl = registryConnection.ProxyUrl ?? "",
                proxyUsername = registryConnection.ProxyUsername ?? "",
                proxyPassword = SettingsRules.Mask(registryConnection.ProxyPassword),
            },
            testMode = new
            {
                testEmails = testMode.TestEmails ?? "",
                stripeSecretKey = SettingsRules.Mask(testMode.StripeSecretKey),
                stripePublishableKey = testMode.StripePublishableKey ?? "",
                stripeReady = testMode.StripeReady,
            },
        };
    }
}
