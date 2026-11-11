using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>
/// Counts of work waiting on staff, shown as badges in the admin sidebar. The same counts head
/// the Today page's groups (<see cref="TodayModule.CountAsync"/>).
/// </summary>
public sealed class SidebarModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/sidebar-counts", async (ApplicationDbContext db) =>
        {
            var c = await TodayModule.CountAsync(db, DateTime.UtcNow);
            // New business names waiting on a person: pay ASIC's BPAY bill, refund a refusal, or look.
            var registrations = await db.BusinessNameRegistrations.CountAsync(r => !r.IsTest
                && (r.Status == BusinessNameRegistrationStatus.AwaitingAsicPayment && r.AirwallexTransferStatus == null
                    || r.Status == BusinessNameRegistrationStatus.Refused
                    || r.Status == BusinessNameRegistrationStatus.NeedsLook
                    || r.Status == BusinessNameRegistrationStatus.Failed));
            return Results.Ok(new
            {
                today = c.Total,
                renewalsToFix = c.RenewalsToFix,
                bpayToPay = c.BpayToPay,
                asicKeyRequests = c.KeyRequests,
                keysToSort = c.KeysToSort,
                clientDuplicates = c.Duplicates,
                registrations,
            });
        }).RequireAuthorization().WithTags("Admin.Sidebar");
    }
}
