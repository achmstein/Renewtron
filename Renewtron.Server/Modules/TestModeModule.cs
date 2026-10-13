using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>
/// Clears out what test customers (Settings → Test mode) left behind: their leads, renewals
/// (with the Stripe test payments) and ASIC key requests. The Business Portal keeps its own
/// copy of the account and renewals; those are removed there.
/// </summary>
public sealed class TestModeModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/test-mode").RequireAuthorization().WithTags("Admin.TestMode");

        group.MapGet("/data", async (ApplicationDbContext db) => Results.Ok(new
        {
            leads = await db.Leads.CountAsync(l => l.IsTest),
            renewals = await db.RenewalRequests.CountAsync(r => r.IsTest),
            asicKeyRequests = await db.AsicKeyRequests.CountAsync(r => r.IsTest),
        }));

        group.MapDelete("/data", async (ApplicationDbContext db) =>
        {
            // Renewals first: they point at the leads (and their payments cascade with them).
            var renewals = await db.RenewalRequests.Where(r => r.IsTest).ExecuteDeleteAsync();
            var keyRequests = await db.AsicKeyRequests.Where(r => r.IsTest).ExecuteDeleteAsync();
            var leads = await db.Leads.Where(l => l.IsTest).ExecuteDeleteAsync();
            return Results.Ok(new { leads, renewals, asicKeyRequests = keyRequests });
        });
    }
}
