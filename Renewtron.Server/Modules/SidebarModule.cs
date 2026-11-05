using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Renewtron.Services;

namespace Renewtron.Modules;

/// <summary>
/// Counts of work waiting on staff, shown as badges in the admin sidebar.
/// </summary>
public sealed class SidebarModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/sidebar-counts", async (ApplicationDbContext db) =>
        {
            // Same test as BPAY to pay's "payable": lodged to pay by BPAY, unpaid, no transfer under way.
            var ended = BpayPaymentService.Ended.Append(BpayPaymentService.Unknown).ToList();
            var bpayToPay = await db.RenewalRequests.AsNoTracking()
                .Where(r => !r.IsTest && r.BpayReference != null && r.BpayReference != ""
                    && r.BpayBillerCode != null && r.BpayBillerCode != "" && r.BpayPaidAt == null)
                .Where(r => r.AirwallexTransferStatus == null || ended.Contains(r.AirwallexTransferStatus))
                .CountAsync();

            // Same queue as the key tool's /manual list: still needs someone to submit it.
            var asicKeyRequests = await db.AsicKeyRequests.AsNoTracking()
                .Where(r => !r.IsTest)
                .Where(r => r.Status == AsicKeyRequestStatus.Pending
                    || r.Status == AsicKeyRequestStatus.Failed
                    || r.Status == AsicKeyRequestStatus.Manual)
                .CountAsync();

            // Clients the linker couldn't tell apart from another on the same ABN.
            var clientDuplicates = await db.Clients.AsNoTracking()
                .CountAsync(c => !c.IsTest && c.PossibleDuplicateOfId != null);

            return Results.Ok(new { bpayToPay, asicKeyRequests, clientDuplicates });
        }).RequireAuthorization().WithTags("Admin.Sidebar");
    }
}
