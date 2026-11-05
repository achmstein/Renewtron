using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class OntraportSalesModule : ICarterModule
{
    /// <summary>Sales still in flight — the page's due-date views only count these.</summary>
    private static readonly OntraportSaleStatus[] PipelineStatuses =
    [
        OntraportSaleStatus.Synced, OntraportSaleStatus.WaitingForRenewalWindow, OntraportSaleStatus.RenewalQueued,
        OntraportSaleStatus.RenewalFailed, OntraportSaleStatus.IneligibleForRenewal, OntraportSaleStatus.AsicNotYetDue,
        OntraportSaleStatus.RenewalInProgress,
    ];

    public sealed record SaleRow(
        Guid Id, string ContactName, string Email, string Abn, string BusinessName, int RenewalYears, decimal AmountPaid,
        string Status, DateTime SyncedAt, DateTime? RenewalDueDate, string? ErrorMessage, Guid? RenewalRequestId,
        string? RenewalStatus, string? RenewalFailedAtStep, string? RenewalErrorMessage, Guid? ClientId);

    private static IQueryable<SaleRow> Project(IQueryable<OntraportSale> q) => q.Select(s => new SaleRow(
        s.Id, s.ContactName, s.Email, s.Abn, s.BusinessName, s.RenewalYears, s.AmountPaid,
        s.Status.ToString(), s.SyncedAt, s.RenewalDueDate, s.ErrorMessage, s.RenewalRequestId,
        s.RenewalRequest != null ? s.RenewalRequest.Status.ToString() : null,
        s.RenewalRequest != null ? s.RenewalRequest.FailedAtStep : null,
        s.RenewalRequest != null ? s.RenewalRequest.ErrorMessage : null,
        s.ClientId));

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ontraport-sales").RequireAuthorization().WithTags("Admin.Ontraport");

        group.MapGet("/", async (ApplicationDbContext db, string? status = null, string? search = null, string? due = null, int page = 1, int pageSize = 25) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
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

            // Due-date views. "Pipeline" = still in flight with a known due date; the buckets
            // match the page's chips (overdue / due in the next 7 or 30 days).
            var now = DateTime.UtcNow;
            IQueryable<OntraportSale> Pipeline() =>
                Filtered().Where(s => PipelineStatuses.Contains(s.Status) && s.RenewalDueDate != null);
            var in8 = now.AddDays(8);
            var in31 = now.AddDays(31);
            IQueryable<OntraportSale> Bucket(string? view) => view switch
            {
                "overdue" => Pipeline().Where(s => s.RenewalDueDate < now),
                "7d" => Pipeline().Where(s => s.RenewalDueDate >= now && s.RenewalDueDate < in8),
                "30d" => Pipeline().Where(s => s.RenewalDueDate >= now && s.RenewalDueDate < in31),
                _ => Filtered(),
            };
            var view = due is "overdue" or "7d" or "30d" ? due : "all";
            var buckets = new
            {
                overdue = await Bucket("overdue").CountAsync(),
                overdueValue = await Bucket("overdue").SumAsync(s => s.AmountPaid),
                soon7 = await Bucket("7d").CountAsync(),
                soon30 = await Bucket("30d").CountAsync(),
                all = await Filtered().CountAsync(),
            };
            var filteredCount = view switch { "overdue" => buckets.overdue, "7d" => buckets.soon7, "30d" => buckets.soon30, _ => buckets.all };

            // Most urgent first; sales with no due date last.
            var items = await Project(Bucket(view)
                    .OrderBy(s => s.RenewalDueDate == null).ThenBy(s => s.RenewalDueDate).ThenByDescending(s => s.SyncedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize))
                .ToListAsync();
            var upNext = await Project(Pipeline().OrderBy(s => s.RenewalDueDate).Take(5)).ToListAsync();

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count)
                    .Select(f => new { value = f.Key.ToString(), count = f.Count }),
            };

            // Header counts, stats and totalCount describe the WHOLE pipeline, not the
            // current filter — counted in SQL over all sales regardless of status/search.
            var byStatus = string.IsNullOrWhiteSpace(search)
                ? statusFacet
                : await db.OntraportSales.AsNoTracking()
                    .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            int CountOf(OntraportSaleStatus st) => byStatus.Where(x => x.Key == st).Sum(x => x.Count);

            var totalCount = byStatus.Sum(x => x.Count);
            var waitingCount = CountOf(OntraportSaleStatus.WaitingForRenewalWindow);
            var queuedCount = CountOf(OntraportSaleStatus.RenewalQueued);
            var completedCount = CountOf(OntraportSaleStatus.RenewalCompleted);
            var failedCount = CountOf(OntraportSaleStatus.RenewalFailed);
            var ineligibleCount = CountOf(OntraportSaleStatus.IneligibleForRenewal);
            var asicNotYetDueCount = CountOf(OntraportSaleStatus.AsicNotYetDue);
            var renewalInProgressCount = CountOf(OntraportSaleStatus.RenewalInProgress);
            var blockedCount = ineligibleCount + asicNotYetDueCount + renewalInProgressCount;

            // Pipeline value next 30 days — paid in Ontraport, not yet completed at ASIC.
            var thirtyDaysAhead = now.AddDays(30);
            var thirtyDaysBack = now.AddDays(-30);
            var pipelineValueNext30d = await db.OntraportSales.AsNoTracking()
                .Where(s => s.RenewalDueDate != null && s.RenewalDueDate <= thirtyDaysAhead && s.RenewalDueDate >= thirtyDaysBack)
                .Where(s => s.Status != OntraportSaleStatus.RenewalCompleted)
                .SumAsync(s => s.AmountPaid);

            // Daily synced volume for last 14 days
            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var dailyMap = (await db.OntraportSales.AsNoTracking()
                    .Where(s => s.SyncedAt >= fourteenDaysAgo)
                    .GroupBy(s => s.SyncedAt.Date)
                    .Select(g => new { Day = g.Key, Count = g.Count() })
                    .ToListAsync())
                .ToDictionary(d => DateOnly.FromDateTime(d.Day), d => d.Count);
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = dailyMap.GetValueOrDefault(DateOnly.FromDateTime(now.Date), 0);
            var yesterdayCount = dailyMap.GetValueOrDefault(DateOnly.FromDateTime(now.Date.AddDays(-1)), 0);
            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            DateTime? lastSyncAt = await db.OntraportSales.AsNoTracking().MaxAsync(s => (DateTime?)s.SyncedAt);
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
                filteredCount, page, pageSize, view, buckets, upNext,
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
