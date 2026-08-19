using Carter;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class SettingsModule : ICarterModule
{
    // Secrets are write-only: GET returns a masked placeholder (last 4 kept for
    // recognition), and a PUT that round-trips the placeholder keeps the stored value.
    // The ASIC CVC never leaves the server at all.
    private const string MaskPrefix = "••••";

    private static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? "" : MaskPrefix + (value.Length > 4 ? value[^4..] : "");

    private static string Unmask(string? incoming, string? current) =>
        incoming != null && incoming.StartsWith(MaskPrefix) ? current ?? "" : incoming ?? "";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings").RequireAuthorization().WithTags("Admin.Settings");

        group.MapGet("/", async (ISettingsService settings) =>
        {
            var sendGrid = await settings.GetSendGridSettingsAsync();
            var stripe = await settings.GetStripeSettingsAsync();
            var asic = await settings.GetAsicSettingsAsync();
            var ontraport = await settings.GetOntraportSettingsAsync();

            return Results.Ok(new
            {
                sendGrid = new { apiKey = Mask(sendGrid.ApiKey), fromEmail = sendGrid.FromEmail, fromName = sendGrid.FromName },
                stripe = new { secretKey = Mask(stripe.SecretKey), publishableKey = stripe.PublishableKey },
                pricing = await settings.GetPricingSettingsAsync(),
                asic = new
                {
                    forceFallback = asic.ForceFallback,
                    email = asic.Email,
                    cardNumber = Mask(asic.CardNumber),
                    cardholderName = asic.CardholderName,
                    expiryMonth = asic.ExpiryMonth,
                    expiryYear = asic.ExpiryYear,
                    // Deliberately never returned. Saving with an empty value keeps the stored one.
                    cvc = "",
                },
                ontraport = new
                {
                    apiAppId = Mask(ontraport.ApiAppId),
                    apiKey = Mask(ontraport.ApiKey),
                    conversationId = ontraport.ConversationId,
                    defaultPollingTimeoutSeconds = ontraport.DefaultPollingTimeoutSeconds,
                },
                winBack = await settings.GetWinBackSettingsAsync(),
                tracking = await settings.GetTrackingSettingsAsync(),
            });
        });

        group.MapPut("/sendgrid", async (SendGridSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetSendGridSettingsAsync();
            body.ApiKey = Unmask(body.ApiKey, current.ApiKey);
            await settings.UpdateSendGridSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/stripe", async (StripeSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetStripeSettingsAsync();
            body.SecretKey = Unmask(body.SecretKey, current.SecretKey);
            await settings.UpdateStripeSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/pricing", async (PricingSettings body, ISettingsService settings) =>
        {
            await settings.UpdatePricingSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/asic", async (AsicSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetAsicSettingsAsync();
            body.CardNumber = Unmask(body.CardNumber, current.CardNumber);
            if (string.IsNullOrEmpty(body.Cvc)) body.Cvc = current.Cvc;
            await settings.UpdateAsicSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/ontraport", async (OntraportSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetOntraportSettingsAsync();
            body.ApiAppId = Unmask(body.ApiAppId, current.ApiAppId);
            body.ApiKey = Unmask(body.ApiKey, current.ApiKey);
            await settings.UpdateOntraportSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/win-back", async (WinBackSettings body, ISettingsService settings) =>
        {
            await settings.UpdateWinBackSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/tracking", async (TrackingSettings body, ISettingsService settings) =>
        {
            await settings.UpdateTrackingSettingsAsync(body);
            return Results.NoContent();
        });
    }
}
