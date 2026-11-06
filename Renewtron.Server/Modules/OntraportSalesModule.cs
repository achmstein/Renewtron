using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;

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
        string? RenewalStatus, string? RenewalFailedAtStep, string? RenewalErrorMessage, Guid? ClientId, DateTime? HandledAt);

    private static IQueryable<SaleRow> Project(IQueryable<OntraportSale> q) => q.Select(s => new SaleRow(
        s.Id, s.ContactName, s.Email, s.Abn, s.BusinessName, s.RenewalYears, s.AmountPaid,
        s.Status.ToString(), s.SyncedAt, s.RenewalDueDate, s.ErrorMessage, s.RenewalRequestId,
        s.RenewalRequest != null ? s.RenewalRequest.Status.ToString() : null,
        s.RenewalRequest != null ? s.RenewalRequest.FailedAtStep : null,
        s.RenewalRequest != null ? s.RenewalRequest.ErrorMessage : null,
        s.ClientId, s.HandledAt));

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ontraport-sales").RequireAuthorization().WithTags("Admin.Ontraport")
            .AddEndpointFilter<RenewalOrderRefreshFilter>();

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
                Filtered().Where(s => PipelineStatuses.Contains(s.Status) && s.HandledAt == null && s.RenewalDueDate != null);
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
                .Where(s => s.Status != OntraportSaleStatus.RenewalCompleted && s.HandledAt == null)
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
        // One sale as its record page shows it: whose it is, the renewal it started, and the
        // ASIC key requests and letters for its business name.
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var s = await db.OntraportSales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();

            var client = await ClientRefs.ByIdAsync(db, s.ClientId) ?? await ClientRefs.ByAbnAsync(db, s.Abn);
            var renewal = s.RenewalRequestId is { } rid
                ? await db.RenewalRequests.AsNoTracking().Where(r => r.Id == rid).Select(r => new
                {
                    id = r.Id,
                    status = r.Status.ToString(),
                    errorCategory = r.ErrorCategory,
                    nextRetryAt = r.NextRetryAt,
                    initiatedAt = r.InitiatedAt,
                    completedAt = r.CompletedAt,
                    bpayReference = r.BpayReference,
                    bpayPaidAt = r.BpayPaidAt,
                    transactionReference = r.TransactionReference,
                }).FirstOrDefaultAsync()
                : null;
            var keyRequests = await db.AsicKeyRequests.AsNoTracking()
                .Where(k => k.OntraportSaleId == id)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new { id = k.Id, status = k.Status.ToString(), createdAt = k.CreatedAt, referenceNumber = k.AsicReferenceNumber, keyReceivedAt = k.KeyReceivedAt })
                .ToListAsync();
            var keyLetters = s.ClientBusinessNameId is { } nameId
                ? await db.AsicKeyNotifications.AsNoTracking()
                    .Where(n => n.ClientBusinessNameId == nameId)
                    .OrderByDescending(n => n.ReceivedAt)
                    .Select(n => new { id = n.Id, status = n.Status.ToString(), receivedAt = n.ReceivedAt, hasKey = n.AsicKey != null })
                    .ToListAsync()
                : [];

            return Results.Ok(new
            {
                id = s.Id,
                client,
                contactName = s.ContactName,
                email = s.Email,
                mobileNumber = s.MobileNumber,
                dateOfBirth = s.DateOfBirth,
                businessName = s.BusinessName,
                abn = s.Abn,
                businessNameOwner = s.BusinessNameOwner,
                address = string.Join(", ", new[] { s.Address, s.Suburb, s.State, s.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x))),
                renewalYears = s.RenewalYears,
                amountPaid = s.AmountPaid,
                renewalDueDate = s.RenewalDueDate,
                status = s.Status.ToString(),
                syncedAt = s.SyncedAt,
                processedAt = s.ProcessedAt,
                errorMessage = s.ErrorMessage,
                handledAt = s.HandledAt,
                handledNote = s.HandledNote,
                ontraportContactId = s.OntraportContactId,
                ontraportTransactionId = s.OntraportTransactionId,
                renewal,
                keyRequests,
                keyLetters,
            });
        });

        // One-off cleanup of old records: marks handled the failed sales and renewals whose name ASIC
        // still lists as registered well after it was due (renewed by hand). dryRun (the default) only counts.
        group.MapPost("/renewed-elsewhere", async (IRenewedElsewhereService service, bool dryRun = true, CancellationToken ct = default) =>
            Results.Ok(await service.MarkAsync(dryRun, ct)));

        // Puts back on the work queues a sale the old-records cleanup marked handled (and its renewal).
        group.MapDelete("/{id:guid}/handled", async (Guid id, ApplicationDbContext db) =>
        {
            var sale = await db.OntraportSales.FirstOrDefaultAsync(x => x.Id == id);
            if (sale is null) return Results.NotFound();
            sale.HandledAt = null;
            sale.HandledNote = null;
            if (sale.RenewalRequestId is { } rid
                && await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == rid) is { } renewal)
            {
                renewal.HandledAt = null;
                renewal.HandledNote = null;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { handledAt = (DateTime?)null });
        });

        // Puts one failed sale back for the next processing run: the single-sale form of
        // requeue-failed. Refused when its last renewal reached ASIC's payment step, since ASIC
        // may already hold the money and a fresh run could pay twice.
        // When the sale already started a renewal, that renewal is retried (one chain of attempts
        // per sale); a fresh one is started only when the sale never got that far.
        group.MapPost("/{id:guid}/requeue", async (Guid id, ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            var sale = await db.OntraportSales.FirstOrDefaultAsync(x => x.Id == id);
            if (sale is null) return Results.NotFound();
            if (sale.Status != OntraportSaleStatus.RenewalFailed)
                return Results.BadRequest(new { error = "Only a failed sale can be tried again." });
            if (sale.RenewalRequestId is { } rid && await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == rid) is { } renewal
                && renewal.Status != RenewalStatus.Completed)
            {
                if (renewal.Status is RenewalStatus.Pending or RenewalStatus.Processing || renewal.NextRetryAt != null)
                    return Results.BadRequest(new { error = "Its renewal is already running or scheduled to try again." });
                if (RenewalErrorClassifier.MayHoldPayment(renewal.FailedAtStep, renewal.HostedTokenizationId, renewal.ErrorCategory))
                    return Results.BadRequest(new { error = "Its last renewal reached ASIC's payment step. Check at ASIC before trying again." });
                renewal.Status = RenewalStatus.Pending;
                renewal.HandledAt = null;
                renewal.HandledNote = null;
                sale.Status = OntraportSaleStatus.RenewalQueued;
                sale.HandledAt = null;
                sale.HandledNote = null;
                await db.SaveChangesAsync();
                var renewalId = renewal.Id;
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewalId));
                return Results.Ok(new { message = "Trying its renewal again now." });
            }

            sale.Status = OntraportSaleStatus.Synced;
            sale.RenewalRequestId = null;
            sale.ProcessedAt = null;
            sale.HandledAt = null;
            sale.HandledNote = null;
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "Back in the queue: the next processing run (07:00, or Process eligible) renews it." });
        });

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
            // Only sales that never started a renewal: one that did is retried through its renewal
            // (renewal Retry / retry-bulk), which keeps one chain of attempts and the payment check.
            var query = db.OntraportSales
                .Where(s => s.Status == OntraportSaleStatus.RenewalFailed && s.HandledAt == null && s.RenewalRequestId == null)
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
                    // Back to the eligible pool for the next processing run.
                    sale.Status = OntraportSaleStatus.Synced;
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
