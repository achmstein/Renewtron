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

        group.MapGet("/", async (ApplicationDbContext db, string? status = null, string? search = null) =>
        {
            // Facet param accepts comma-separated multi-values ("RenewalFailed,RenewalQueued").
            var statusValues = Helpers.ParseEnumList<OntraportSaleStatus>(status);

            // One filter pipeline, with the status dimension excludable so its own option
            // counts can be computed under the search term alone (classic faceted search).
            IQueryable<OntraportSale> Filtered(bool applyStatus = true)
            {
                IQueryable<OntraportSale> q = db.OntraportSales.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLower();
                    q = q.Where(s =>
                        s.BusinessName.ToLower().Contains(term) ||
                        s.ContactName.ToLower().Contains(term) ||
                        s.Abn.ToLower().Contains(term) ||
                        s.Email.ToLower().Contains(term));
                }
                if (applyStatus && statusValues.Length > 0)
                    q = q.Where(s => statusValues.Contains(s.Status));
                return q;
            }

            var items = await Filtered()
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

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count)
                    .Select(f => new { value = f.Key.ToString(), count = f.Count }),
            };

            // Header counts, stats and totalCount describe the WHOLE pipeline, not the
            // current filter — computed over all sales regardless of status/search.
            var all = await db.OntraportSales.AsNoTracking()
                .Select(s => new { s.Status, s.AmountPaid, s.SyncedAt, s.RenewalDueDate })
                .ToListAsync();

            var totalCount = all.Count;
            var waitingCount = all.Count(s => s.Status == OntraportSaleStatus.WaitingForRenewalWindow);
            var queuedCount = all.Count(s => s.Status == OntraportSaleStatus.RenewalQueued);
            var completedCount = all.Count(s => s.Status == OntraportSaleStatus.RenewalCompleted);
            var failedCount = all.Count(s => s.Status == OntraportSaleStatus.RenewalFailed);
            var ineligibleCount = all.Count(s => s.Status == OntraportSaleStatus.IneligibleForRenewal);
            var asicNotYetDueCount = all.Count(s => s.Status == OntraportSaleStatus.AsicNotYetDue);
            var renewalInProgressCount = all.Count(s => s.Status == OntraportSaleStatus.RenewalInProgress);
            var blockedCount = ineligibleCount + asicNotYetDueCount + renewalInProgressCount;

            // Pipeline value next 30 days — paid in Ontraport, not yet completed at ASIC.
            var now = DateTime.UtcNow;
            var thirtyDaysAhead = now.AddDays(30);
            var pipelineValueNext30d = all
                .Where(s => s.RenewalDueDate.HasValue && s.RenewalDueDate.Value <= thirtyDaysAhead && s.RenewalDueDate.Value >= now.AddDays(-30))
                .Where(s => s.Status != OntraportSaleStatus.RenewalCompleted)
                .Sum(s => s.AmountPaid);

            // Daily synced volume for last 14 days
            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var dailyMap = all
                .Where(s => s.SyncedAt >= fourteenDaysAgo)
                .GroupBy(s => DateOnly.FromDateTime(s.SyncedAt))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = all.Count(s => s.SyncedAt.Date == now.Date);
            var yesterdayCount = all.Count(s => s.SyncedAt.Date == now.Date.AddDays(-1));
            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            DateTime? lastSyncAt = all.Count > 0 ? all.Max(s => s.SyncedAt) : null;
            // Next sync — the recurring job runs at 06:00 AEST. Compute next AEST 06:00 in UTC.
            var aest = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
            var aestNow = TimeZoneInfo.ConvertTimeFromUtc(now, aest);
            var todaySix = new DateTime(aestNow.Year, aestNow.Month, aestNow.Day, 6, 0, 0, DateTimeKind.Unspecified);
            var nextSixLocal = aestNow < todaySix ? todaySix : todaySix.AddDays(1);
            var nextSyncAt = TimeZoneInfo.ConvertTimeToUtc(nextSixLocal, aest);

            return Results.Ok(new
            {
                totalCount, waitingCount, queuedCount, completedCount, failedCount,
                ineligibleCount, asicNotYetDueCount, renewalInProgressCount, blockedCount,
                items,
                facets,
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

        // Puts failed sales back into the processor's eligible pool in controlled batches.
        // Most failures are transient (network blips, the fixed Set-Cookie bug), so a
        // re-run usually clears them. dryRun defaults to true — a plain POST only previews.
        group.MapPost("/requeue-failed", async (
            ApplicationDbContext db,
            bool dryRun = true,
            int max = 50,
            bool includePastDue = false,
            string? errorContains = null) =>
        {
            max = Math.Clamp(max, 1, 500);
            var pastDueCutoff = DateTime.UtcNow.AddDays(-30);

            // The processor needs a due date to act; sales past the -30d window would only
            // churn to NotDueForRenewal, so they're excluded unless explicitly requested.
            var query = db.OntraportSales
                .Where(s => s.Status == OntraportSaleStatus.RenewalFailed)
                .Where(s => s.RenewalDueDate != null);
            if (!includePastDue)
                query = query.Where(s => s.RenewalDueDate >= pastDueCutoff);
            if (!string.IsNullOrWhiteSpace(errorContains))
                query = query.Where(s => s.ErrorMessage != null && s.ErrorMessage.Contains(errorContains));

            var totalMatching = await query.CountAsync();
            var batch = await query.OrderBy(s => s.RenewalDueDate).Take(max).ToListAsync();

            if (!dryRun)
            {
                foreach (var sale in batch)
                {
                    // Back to the eligible pool. The old RenewalRequest row (if any) keeps
                    // its failure history; clearing the link makes the processor attempt fresh.
                    sale.Status = OntraportSaleStatus.Synced;
                    sale.RenewalRequestId = null;
                    sale.ProcessedAt = null;
                }
                await db.SaveChangesAsync();
            }

            return Results.Ok(new
            {
                dryRun,
                totalMatching,
                batchSize = batch.Count,
                requeued = dryRun ? 0 : batch.Count,
                note = "Re-queued sales are picked up by the next 07:00 processing run, or immediately via Process eligible.",
                items = batch.Select(s => new
                {
                    id = s.Id,
                    businessName = s.BusinessName,
                    abn = s.Abn,
                    amountPaid = s.AmountPaid,
                    renewalDueDate = s.RenewalDueDate,
                    error = s.ErrorMessage,
                }),
            });
        });
    }
}
