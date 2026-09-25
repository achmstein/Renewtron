using Carter;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

/// <summary>
/// Admin view of the outbound ASIC key requests: what's queued, what ASIC accepted, what
/// failed and why, plus the buttons to run the queue, resend one row, or queue a sale.
/// Submissions run as Hangfire jobs because each is a browser session of a minute or so,
/// longer than the proxy in front of the API will wait; the UI polls /jobs/{id}.
/// </summary>
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
                        r.Email.ToLower().Contains(term) ||
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
                .Select(r => ToDetail(r))
                .ToListAsync();

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count)
                    .Select(f => new { value = f.Key.ToString(), count = f.Count }),
            };

            var all = await db.AsicKeyRequests.AsNoTracking()
                .Select(r => new { r.Status, r.CreatedAt, r.SubmittedAt, r.CanAutoRetry })
                .ToListAsync();

            var now = DateTime.UtcNow;
            var totalCount = all.Count;
            var pendingCount = all.Count(r => r.Status == AsicKeyRequestStatus.Pending);
            var submittedCount = all.Count(r => r.Status == AsicKeyRequestStatus.Submitted);
            var keyReceivedCount = all.Count(r => r.Status == AsicKeyRequestStatus.KeyReceived);
            var failedCount = all.Count(r => r.Status == AsicKeyRequestStatus.Failed);
            var stuckCount = all.Count(r => r.Status == AsicKeyRequestStatus.Failed && !r.CanAutoRetry);

            var fourteenDaysAgo = now.Date.AddDays(-13);
            var dailyMap = all
                .Where(r => r.SubmittedAt != null && r.SubmittedAt >= fourteenDaysAgo)
                .GroupBy(r => DateOnly.FromDateTime(r.SubmittedAt!.Value))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = all.Count(r => r.SubmittedAt?.Date == now.Date);
            var yesterdayCount = all.Count(r => r.SubmittedAt?.Date == now.Date.AddDays(-1));
            decimal? deltaPct = yesterdayCount > 0
                ? Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1)
                : null;

            var job = RunJobInfo();

            return Results.Ok(new
            {
                totalCount, pendingCount, submittedCount, keyReceivedCount, failedCount, stuckCount,
                items,
                facets,
                stats = new
                {
                    lastRunAt = job?.LastExecution,
                    lastRunState = job?.LastJobState,
                    lastRunError = job?.Error,
                    nextRunAt = job?.NextExecution,
                    runCron = job?.Cron,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                },
            });
        });

        // The queue run — the same thing the recurring job does — as a tracked job.
        group.MapPost("/run", (IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<IAsicKeyRequestService>(s => s.ProcessPendingAsync(CancellationToken.None));
            return Results.Accepted(value: new { jobId, message = "Run queued." });
        });

        // Sends one row now, whatever its state, as a tracked job (a browser session takes a minute).
        group.MapPost("/{id:guid}/submit", async (Guid id, ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            if (!await db.AsicKeyRequests.AnyAsync(r => r.Id == id)) return Results.NotFound();
            var jobId = jobs.Enqueue<IAsicKeyRequestService>(s => s.SubmitAsync(id, CancellationToken.None));
            return Results.Accepted(value: new { jobId, message = "Submission queued." });
        });

        // Puts a failed row back in the queue for the next run without sending it now.
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

        // Queues (or re-opens) the request for a sale; with submitNow it goes out as a tracked job.
        group.MapPost("/for-sale/{saleId:guid}", async (Guid saleId, bool submitNow, IAsicKeyRequestService service, IBackgroundJobClient jobs, CancellationToken ct) =>
        {
            try
            {
                var row = await service.QueueForSaleAsync(saleId, submitNow: false, ct);
                string? jobId = null;
                if (submitNow)
                    jobId = jobs.Enqueue<IAsicKeyRequestService>(s => s.SubmitAsync(row.Id, CancellationToken.None));
                return Results.Ok(new { request = ToDetail(row), jobId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400);
            }
        });

        group.MapGet("/jobs/{jobId}", (string jobId) =>
        {
            using var connection = JobStorage.Current.GetConnection();
            var job = connection.GetJobData(jobId);
            if (job == null) return Results.NotFound();

            var state = job.State ?? "Unknown";
            var done = state is "Succeeded" or "Failed" or "Deleted";
            var stateData = connection.GetStateData(jobId);

            object? result = null;
            string? error = null;
            if (state == "Succeeded" && stateData?.Data.TryGetValue("Result", out var json) == true && !string.IsNullOrWhiteSpace(json))
            {
                // A run returns a summary; a single submission returns the row. Try both shapes.
                try { result = SerializationHelper.Deserialize<AsicKeyRequestRunResult>(json, SerializationOption.User); }
                catch
                {
                    try
                    {
                        var row = SerializationHelper.Deserialize<AsicKeyRequest>(json, SerializationOption.User);
                        if (row != null) result = ToDetail(row);
                    }
                    catch { /* the row list still tells the story */ }
                }
            }
            if (state == "Failed" && stateData != null)
                error = stateData.Data.TryGetValue("ExceptionMessage", out var message) ? message : stateData.Reason;

            return Results.Ok(new { jobId, state, done, result, error });
        });
    }

    public static object ToDetail(AsicKeyRequest r) => new
    {
        id = r.Id,
        ontraportSaleId = r.OntraportSaleId,
        ontraportContactId = r.OntraportContactId,
        businessName = r.BusinessName,
        abn = r.Abn,
        contactName = (r.GivenNames + " " + r.FamilyName).Trim(),
        email = r.Email,
        phone = r.Phone,
        question = r.Question,
        source = r.Source,
        status = r.Status.ToString(),
        asicReferenceNumber = r.AsicReferenceNumber,
        errorMessage = r.ErrorMessage,
        canAutoRetry = r.CanAutoRetry,
        captchaSolves = r.CaptchaSolves,
        asicKeyNotificationId = r.AsicKeyNotificationId,
        attemptCount = r.AttemptCount,
        createdAt = r.CreatedAt,
        processedAt = r.ProcessedAt,
        submittedAt = r.SubmittedAt,
        keyReceivedAt = r.KeyReceivedAt,
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
