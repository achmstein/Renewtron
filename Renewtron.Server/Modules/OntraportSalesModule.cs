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
                    renewalRequestId = s.RenewalRequestId,
                    renewalStatus = s.RenewalRequest != null ? s.RenewalRequest.Status.ToString() : null,
                    renewalFailedAtStep = s.RenewalRequest != null ? s.RenewalRequest.FailedAtStep : null,
                    renewalErrorMessage = s.RenewalRequest != null ? s.RenewalRequest.ErrorMessage : null,
                })
                .ToListAsync();

            var totalCount = sales.Count;
            var waitingCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.WaitingForRenewalWindow));
            var queuedCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.RenewalQueued));
            var completedCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.RenewalCompleted));
            var failedCount = sales.Count(s => s.status == nameof(OntraportSaleStatus.RenewalFailed));

            // Pipeline value next 30 days — paid in Ontraport, not yet completed at ASIC.
            var now = DateTime.UtcNow;
            var thirtyDaysAhead = now.AddDays(30);
            var pipelineValueNext30d = sales
                .Where(s => s.renewalDueDate.HasValue && s.renewalDueDate.Value <= thirtyDaysAhead && s.renewalDueDate.Value >= now.AddDays(-30))
                .Where(s => s.status != nameof(OntraportSaleStatus.RenewalCompleted))
                .Sum(s => s.amountPaid);

            // Daily synced volume for last 14 days
            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var dailyMap = sales
                .Where(s => s.syncedAt >= fourteenDaysAgo)
                .GroupBy(s => DateOnly.FromDateTime(s.syncedAt))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = sales.Count(s => s.syncedAt.Date == now.Date);
            var yesterdayCount = sales.Count(s => s.syncedAt.Date == now.Date.AddDays(-1));
            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            DateTime? lastSyncAt = sales.Count > 0 ? sales.Max(s => s.syncedAt) : null;
            // Next sync — the recurring job runs at 06:00 AEST. Compute next AEST 06:00 in UTC.
            var aest = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
            var aestNow = TimeZoneInfo.ConvertTimeFromUtc(now, aest);
            var todaySix = new DateTime(aestNow.Year, aestNow.Month, aestNow.Day, 6, 0, 0, DateTimeKind.Unspecified);
            var nextSixLocal = aestNow < todaySix ? todaySix : todaySix.AddDays(1);
            var nextSyncAt = TimeZoneInfo.ConvertTimeToUtc(nextSixLocal, aest);

            return Results.Ok(new
            {
                totalCount, waitingCount, queuedCount, completedCount, failedCount,
                items = sales,
                stats = new
                {
                    pipelineValueNext30d,
                    lastSyncAt,
                    nextSyncAt,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                },
            });
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
