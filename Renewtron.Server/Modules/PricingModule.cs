using Carter;
using Microsoft.Extensions.Options;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class PricingModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing", (IOptionsSnapshot<PricingSettings> pricing) =>
        {
            var p = pricing.Value;
            return Results.Ok(new { oneYearFee = p.OneYearFee, threeYearFee = p.ThreeYearFee });
        }).WithTags("Wizard");
    }
}
