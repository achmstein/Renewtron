using Ato.Api.Contracts;
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
                atoAgent = await settings.GetAtoAgentSettingsAsync(),
                winBack = await settings.GetWinBackSettingsAsync(),
            });
        });

        // Lists tax agents the live ATO session is registered against, so the
        // Settings UI can present a real dropdown instead of a free-text ABN.
        group.MapGet("/ato-agents", async (AtoApi.AtoApiClient ato) =>
        {
            try
            {
                var state = await ato.GetSessionStateAsync(new GetSessionStateRequest());
                if (state.Phase != SessionPhase.Authenticated)
                {
                    return Results.Ok(new
                    {
                        authenticated = false,
                        phase = state.Phase.ToString(),
                        agents = Array.Empty<object>(),
                        selectedAgentAbn = (string?)null,
                    });
                }
                return Results.Ok(new
                {
                    authenticated = true,
                    phase = state.Phase.ToString(),
                    agents = state.Agents.Select(a => new { abn = a.Abn, name = a.Name }).ToArray(),
                    selectedAgentAbn = state.SelectedAgentAbn,
                });
            }
            catch (Grpc.Core.RpcException ex)
            {
                return Results.Problem(
                    title: "Could not reach Ato.Api",
                    detail: ex.Status.Detail,
                    statusCode: 502);
            }
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

        group.MapPut("/ato-agent", async (AtoAgentSettings body, ISettingsService settings) =>
        {
            await settings.UpdateAtoAgentSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/win-back", async (WinBackSettings body, ISettingsService settings) =>
        {
            await settings.UpdateWinBackSettingsAsync(body);
            return Results.NoContent();
        });
    }
}
