using Carter;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class SettingsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings").RequireAuthorization().WithTags("Admin.Settings");

        group.MapGet("/", async (ISettingsService settings) =>
        {
            return Results.Ok(new
            {
                sendGrid = await settings.GetSendGridSettingsAsync(),
                stripe = await settings.GetStripeSettingsAsync(),
                pricing = await settings.GetPricingSettingsAsync(),
                asic = await settings.GetAsicSettingsAsync(),
                ontraport = await settings.GetOntraportSettingsAsync(),
            });
        });

        group.MapPut("/sendgrid", async (SendGridSettings body, ISettingsService settings) =>
        {
            await settings.UpdateSendGridSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/stripe", async (StripeSettings body, ISettingsService settings) =>
        {
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
            await settings.UpdateAsicSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/ontraport", async (OntraportSettings body, ISettingsService settings) =>
        {
            await settings.UpdateOntraportSettingsAsync(body);
            return Results.NoContent();
        });
    }
}
