using Asic.Integrations.Registry;
using Carter;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Services;
using Renewtron.Services.AsicPortal;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// Admin → Settings. Each section saves by: load what's stored, let <see cref="SettingsRules"/>
/// un-mask, tidy and check the form, then write the override. The rules live there, not here.
/// </summary>
public sealed class SettingsModule : ICarterModule
{
    public record TwoCaptchaTestBody(string? TwoCaptchaApiKey);

    private static IResult Saved(string? error) =>
        error is null ? Results.NoContent() : Results.BadRequest(new { error });

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings").RequireAuthorization().WithTags("Admin.Settings");

        group.MapGet("/", async (AdminSettingsView view) => Results.Ok(await view.LoadAsync()));

        group.MapPut("/sendgrid", async (SendGridSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareSendGrid(body, await settings.GetSendGridSettingsAsync()) is { } error) return Saved(error);
            await settings.UpdateSendGridSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/stripe", async (StripeSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareStripe(body, await settings.GetStripeSettingsAsync()) is { } error) return Saved(error);
            await settings.UpdateStripeSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/pricing", async (PricingSettings body, ISettingsService settings) =>
        {
            await settings.UpdatePricingSettingsAsync(body);
            return Saved(null);
        });

        // The card (or BPAY) Renewtron pays ASIC with, and ASIC's fees.
        group.MapPut("/asic", async (AsicSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareAsic(body, await settings.GetAsicSettingsAsync()) is { } error) return Saved(error);
            await settings.UpdateAsicSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/ontraport", async (OntraportSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareOntraport(body, await settings.GetOntraportSettingsAsync()) is { } error) return Saved(error);
            await settings.UpdateOntraportSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/win-back", async (WinBackSettings body, ISettingsService settings) =>
        {
            await settings.UpdateWinBackSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/tracking", async (TrackingSettings body, ISettingsService settings) =>
        {
            await settings.UpdateTrackingSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/asic-key-inbox", async (AsicKeyInboxSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareAsicKeyInbox(body, await settings.GetAsicKeyInboxSettingsAsync()) is { } error) return Saved(error);
            await settings.UpdateAsicKeyInboxSettingsAsync(body);
            return Saved(null);
        });

        group.MapPost("/asic-key-inbox/test", async (AsicKeyInboxSettings body, AsicKeyInboxTester tester, CancellationToken ct) =>
            Results.Ok(await tester.TestAsync(body, ct)));

        group.MapPut("/asic-key-request", async (AsicKeyRequestSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareAsicKeyRequest(body) is { } error) return Saved(error);
            await settings.UpdateAsicKeyRequestSettingsAsync(body);
            return Saved(null);
        });

        // The ASIC Connect login for "Link to our ASIC account" (not the renewals' card).
        group.MapPut("/asic-portal", async (SettingsRules.AsicPortalForm body, IOptionsMonitor<AsicPortalSettings> current, ISettingsService settings) =>
        {
            var (saved, error) = SettingsRules.PrepareAsicPortal(body, current.CurrentValue);
            if (error is not null) return Saved(error);
            await settings.UpdateAsicPortalSettingsAsync(saved);
            return Saved(null);
        });

        // Logs in with the saved login and out again: run it first on a new server.
        group.MapPost("/asic-portal/test", async (IAsicPortalLinkService service, CancellationToken ct) =>
        {
            var problem = await service.TestLoginAsync(ct);
            return Results.Ok(new { ok = problem is null, message = problem ?? "Logged in to ASIC Connect and out again." });
        });

        // Registering new business names from the public /register page.
        group.MapPut("/registration", async (RegistrationSettings body, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareRegistration(body) is { } error) return Saved(error);
            await settings.UpdateRegistrationSettingsAsync(body);
            return Saved(null);
        });

        group.MapPut("/airwallex", async (AirwallexSettings body, IOptionsMonitor<AirwallexSettings> current, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareAirwallex(body, current.CurrentValue) is { } error) return Saved(error);
            await settings.UpdateAirwallexSettingsAsync(body);
            return Saved(null);
        });

        // Logs in with the saved keys, so a wrong key shows up before anyone tries to pay.
        group.MapPost("/airwallex/test", async (IAirwallexClient airwallex) =>
        {
            var error = await airwallex.TestConnectionAsync();
            return error is null ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error });
        });

        // ASIC's register search, which reads names' real renewal dates (behind reCAPTCHA).
        group.MapPut("/asic-registry", async (AsicRegistrySection body, IOptionsMonitor<AsicRegistryOptions> current, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareAsicRegistry(body, current.CurrentValue) is { } error) return Saved(error);
            await settings.UpdateAsicRegistrySettingsAsync(body);
            return Saved(null);
        });

        // Reads the 2Captcha balance for the key in the form (masked = the saved key), so a
        // wrong or empty account shows up before the nightly due-date run hits a CAPTCHA.
        group.MapPost("/asic-registry/test", async (TwoCaptchaTestBody body, IOptionsMonitor<AsicRegistryOptions> current) =>
        {
            var key = SettingsRules.Unmask(body.TwoCaptchaApiKey, current.CurrentValue.TwoCaptchaApiKey);
            var result = await TwoCaptchaSolver.GetBalanceAsync(key);
            return Results.Ok(new { ok = result.Error is null, balance = result.Balance, error = result.Error });
        });

        group.MapPut("/test-mode", async (TestModeSettings body, IOptionsMonitor<TestModeSettings> current, ISettingsService settings) =>
        {
            if (SettingsRules.PrepareTestMode(body, current.CurrentValue) is { } error) return Saved(error);
            await settings.UpdateTestModeSettingsAsync(body);
            return Saved(null);
        });
    }
}
