using Carter;
using Renewtron.Services;

namespace Renewtron.Modules;

/// <summary>Paid renewals as one record each (see <see cref="Data.RenewalOrder"/>).</summary>
public sealed class RenewalOrdersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/renewal-orders").RequireAuthorization().WithTags("Admin.RenewalOrders");

        // Builds or refreshes the orders from the sale, bulk and renewal records. dryRun (the
        // default) only reports counts per stage and reason.
        group.MapPost("/rebuild", async (IRenewalOrderService service, bool dryRun = true, CancellationToken ct = default) =>
            Results.Ok(await service.RebuildAsync(dryRun, ct)));
    }
}
