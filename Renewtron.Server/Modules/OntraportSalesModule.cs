using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class OntraportSalesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ontraport-sales").RequireAuthorization().WithTags("Admin.Ontraport");

        group.MapGet("/", async (ApplicationDbContext db) =>
        {
            var sales = await db.OntraportSales.AsNoTracking()
                .OrderByDescending(s => s.SyncedAt)
                .Select(s => new
                {
                    id = s.Id,
                    contactName = s.ContactName,
                    email = s.Email,
                    abn = s.Abn,
                    businessName = s.BusinessName,
                    renewalYears = s.RenewalYears,
                    amountPaid = s.AmountPaid,
                    status = s.Status.ToString(),
                    syncedAt = s.SyncedAt,
                    renewalDueDate = s.RenewalDueDate,
                    errorMessage = s.ErrorMessage,
                })
                .ToListAsync();

            var totalCount = sales.Count;
            var waitingCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.WaitingForRenewalWindow));
            var queuedCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.RenewalQueued));
            var completedCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.RenewalCompleted));
            var failedCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.RenewalFailed));

            return Results.Ok(new { totalCount, waitingCount, queuedCount, completedCount, failedCount, items = sales });
        });

        group.MapPost("/sync", async (IOntraportSalesService service) =>
        {
            try
            {
                var synced = await service.SyncSalesAsync();
                return Results.Ok(new { syncedCount = synced.Count, message = $"Synced {synced.Count} new sales from Ontraport." });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        group.MapPost("/process-eligible", (IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<IOntraportSalesService>(s => s.ProcessEligibleRenewalsAsync());
            return Results.Accepted(value: new { jobId, message = "Processing job queued. Eligible renewals will be processed in the background. Refresh to see results." });
        });
    }
}
