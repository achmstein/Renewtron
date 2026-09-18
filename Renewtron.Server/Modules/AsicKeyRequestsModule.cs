using Carter;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class AsicKeyRequestsModule : ICarterModule
{
    public const string RecurringJobId = "asic-key-request-run";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/asic-key-requests").RequireAuthorization().WithTags("Admin.AsicKeyRequests");

        group.MapGet("/", async (ApplicationDbContext db, string? status = null, string? search = null) =>
        {
            var statusValues = Helpers.ParseEnumList<AsicKeyRequestStatus>(status);

            IQueryable<AsicKeyRequest> Filtered(bool applyStatus = true)
            {
                IQueryable<AsicKeyRequest> q = db.AsicKeyRequests.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLower();
                    q = q.Where(r =>
                        r.BusinessName.ToLower().Contains(term) ||
                        r.Abn.Contains(term) ||
                        (r.GivenNames + " " + r.FamilyName).ToLower().Contains(term) ||
                        (r.OntraportContactId != null && r.OntraportContactId.Contains(term)) ||
                        (r.AsicReferenceNumber != null && r.AsicReferenceNumber.Contains(term)));
                }
                if (applyStatus && statusValues.Length > 0)
                    q = q.Where(r => statusValues.Contains(r.Status));
                return q;
            }

            var items = await Filtered()
                .OrderByDescending(r => r.CreatedAt)
                .Take(500)
                .Select(r => new
                {
                    id = r.Id,
                    ontraportSaleId = r.OntraportSaleId,
                    ontraportContactId = r.OntraportContactId,
                    contactName = (r.GivenNames + " " + r.FamilyName).Trim(),
                    phone = r.Phone,
                    abn = r.Abn,
                    businessName = r.BusinessName,
                    question = r.Question,
                    source = r.Source,
                    status = r.Status.ToString(),
                    asicReferenceNumber = r.AsicReferenceNumber,
                    errorMessage = r.ErrorMessage,
                    canAutoRetry = r.CanAutoRetry,
                    captchaSolves = r.CaptchaSolves,
                    asicKeyNotificationId = r.AsicKeyNotificationId,
                    asicKey = r.AsicKeyNotification != null ? r.AsicKeyNotification.AsicKey : null,
                    attemptCount = r.AttemptCount,
                    createdAt = r.CreatedAt,
                    processedAt = r.ProcessedAt,
                    submittedAt = r.SubmittedAt,
                    keyReceivedAt = r.KeyReceivedAt,
                })
                .ToListAsync();

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count)
                    .Select(f => new { value = f.Key.ToString(), count = f.Count }),
            };

            // Header counts describe the whole table, not the current filter.
            var all = await db.AsicKeyRequests.AsNoTracking()
                .Select(r => new { r.Status, r.CreatedAt, r.SubmittedAt, r.KeyReceivedAt, r.CaptchaSolves })
                .ToListAsync();

            var now = DateTime.UtcNow;
            var totalCount = all.Count;
            var pendingCount = all.Count(r => r.Status == AsicKeyRequestStatus.Pending);
            var submittedCount = all.Count(r => r.Status == AsicKeyRequestStatus.Submitted);
            var keyReceivedCount = all.Count(r => r.Status == AsicKeyRequestStatus.KeyReceived);
            var failedCount = all.Count(r => r.Status == AsicKeyRequestStatus.Failed);

            var turnarounds = all
                .Where(r => r.Status == AsicKeyRequestStatus.KeyReceived && r.SubmittedAt != null && r.KeyReceivedAt != null && r.SubmittedAt >= now.AddDays(-90))
                .Select(r => (r.KeyReceivedAt!.Value - r.SubmittedAt!.Value).TotalHours)
                .Where(h => h >= 0)
                .ToList();
            double? avgTurnaroundHours = turnarounds.Count > 0 ? Math.Round(turnarounds.Average(), 1) : null;

            var fourteenDaysAgo = now.Date.AddDays(-13);
            var dailyMap = all
                .Where(r => r.CreatedAt >= fourteenDaysAgo)
                .GroupBy(r => DateOnly.FromDateTime(r.CreatedAt))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = all.Count(r => r.CreatedAt.Date == now.Date);
            var yesterdayCount = all.Count(r => r.CreatedAt.Date == now.Date.AddDays(-1));
            decimal? deltaPct = yesterdayCount > 0
                ? Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1)
                : null;

            var job = RunJobInfo();

            return Results.Ok(new
            {
                totalCount, pendingCount, submittedCount, keyReceivedCount, failedCount,
                items,
                facets,
                stats = new
                {
                    lastRunAt = job?.LastExecution,
                    lastRunState = job?.LastJobState,
                    lastRunError = job?.Error,
                    nextRunAt = job?.NextExecution,
                    runCron = job?.Cron,
                    avgTurnaroundHours,
                    captchaSolves = all.Sum(r => r.CaptchaSolves),
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                },
            });
        });

        // Runs as a Hangfire job: each submission buys a captcha and walks three ASIC pages,
        // so a batch easily outlasts a proxy timeout. The UI polls /jobs/{id}.
        group.MapPost("/run", (IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<IAsicKeyRequestService>(s => s.ProcessPendingAsync(CancellationToken.None));
            return Results.Accepted(value: new { jobId, message = "Run queued." });
        });

        group.MapPost("/retry-failed", async (ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            var rows = await db.AsicKeyRequests.Where(r => r.Status == AsicKeyRequestStatus.Failed).ToListAsync();
            foreach (var row in rows)
            {
                row.Status = AsicKeyRequestStatus.Pending;
                row.CanAutoRetry = true;
            }
            await db.SaveChangesAsync();

            var jobId = jobs.Enqueue<IAsicKeyRequestService>(s => s.ProcessPendingAsync(CancellationToken.None));
            return Results.Accepted(value: new { jobId, requeued = rows.Count, message = $"{rows.Count} request(s) queued for retry." });
        });

        group.MapGet("/jobs/{jobId}", (string jobId) =>
        {
            using var connection = JobStorage.Current.GetConnection();
            var job = connection.GetJobData(jobId);
            if (job == null) return Results.NotFound();

            var state = job.State ?? "Unknown";
            var done = state is "Succeeded" or "Failed" or "Deleted";
            var stateData = connection.GetStateData(jobId);

            AsicKeyRequestRunResult? result = null;
            string? error = null;
            if (state == "Succeeded" && stateData?.Data.TryGetValue("Result", out var json) == true && !string.IsNullOrWhiteSpace(json))
            {
                try { result = SerializationHelper.Deserialize<AsicKeyRequestRunResult>(json, SerializationOption.User); }
                catch { /* the row list still tells the story */ }
            }
            if (state == "Failed" && stateData != null)
                error = stateData.Data.TryGetValue("ExceptionMessage", out var message) ? message : stateData.Reason;

            return Results.Ok(new { jobId, state, done, result, error });
        });

        // Sends one request to ASIC right now (any status) — the per-row "Submit"/"Retry".
        group.MapPost("/{id:guid}/submit", async (Guid id, IAsicKeyRequestService service, CancellationToken ct) =>
        {
            try
            {
                var row = await service.SubmitAsync(id, ct);
                return row == null ? Results.NotFound() : Results.Ok(ToDetail(row));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // Back to Pending for the next scheduled run, without spending a captcha now.
        group.MapPost("/{id:guid}/requeue", async (Guid id, ApplicationDbContext db) =>
        {
            var row = await db.AsicKeyRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return Results.NotFound();
            row.Status = AsicKeyRequestStatus.Pending;
            row.ErrorMessage = null;
            row.CanAutoRetry = true;
            await db.SaveChangesAsync();
            return Results.Ok(ToDetail(row));
        });

        // The "Request ASIC key" button on an Ontraport sale: creates (or re-opens) the
        // request and submits it inline so the operator sees the reference number.
        group.MapPost("/for-sale/{saleId:guid}", async (Guid saleId, IAsicKeyRequestService service, bool submitNow = true, CancellationToken ct = default) =>
        {
            try
            {
                var row = await service.QueueForSaleAsync(saleId, submitNow, ct);
                return Results.Ok(ToDetail(row));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 404);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static object ToDetail(AsicKeyRequest r) => new
    {
        id = r.Id,
        ontraportSaleId = r.OntraportSaleId,
        businessName = r.BusinessName,
        abn = r.Abn,
        status = r.Status.ToString(),
        asicReferenceNumber = r.AsicReferenceNumber,
        errorMessage = r.ErrorMessage,
        canAutoRetry = r.CanAutoRetry,
        captchaSolves = r.CaptchaSolves,
        attemptCount = r.AttemptCount,
        processedAt = r.ProcessedAt,
        submittedAt = r.SubmittedAt,
    };

    private static RecurringJobDto? RunJobInfo()
    {
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            return connection.GetRecurringJobs().FirstOrDefault(j => j.Id == RecurringJobId);
        }
        catch
        {
            return null;
        }
    }
}
