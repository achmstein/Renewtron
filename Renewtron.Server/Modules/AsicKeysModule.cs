using Carter;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class AsicKeysModule : ICarterModule
{
    public const string RecurringJobId = "asic-key-inbox-scan";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/asic-keys").RequireAuthorization().WithTags("Admin.AsicKeys");

        group.MapGet("/", async (ApplicationDbContext db, string? status = null, string? search = null) =>
        {
            var statusValues = Helpers.ParseEnumList<AsicKeyNotificationStatus>(status);

            // Same faceted shape as the Ontraport page: status counts are computed under the
            // search term alone so the facet shows what each option would yield.
            IQueryable<AsicKeyNotification> Filtered(bool applyStatus = true)
            {
                IQueryable<AsicKeyNotification> q = db.AsicKeyNotifications.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLower();
                    q = q.Where(n =>
                        (n.BusinessName != null && n.BusinessName.ToLower().Contains(term)) ||
                        (n.Abn != null && n.Abn.Contains(term)) ||
                        (n.AsicKey != null && n.AsicKey.ToLower().Contains(term)) ||
                        (n.OntraportContactIds != null && n.OntraportContactIds.Contains(term)) ||
                        (n.Subject != null && n.Subject.ToLower().Contains(term)));
                }
                if (applyStatus && statusValues.Length > 0)
                    q = q.Where(n => statusValues.Contains(n.Status));
                return q;
            }

            var items = await Filtered()
                .OrderByDescending(n => n.ReceivedAt)
                .Take(500)
                .Select(n => new
                {
                    id = n.Id,
                    businessName = n.BusinessName,
                    abn = n.Abn,
                    asicKey = n.AsicKey,
                    ontraportContactIds = n.OntraportContactIds,
                    ontraportContactsUpdated = n.OntraportContactsUpdated,
                    status = n.Status.ToString(),
                    errorMessage = n.ErrorMessage,
                    pdfTextExcerpt = n.PdfTextExcerpt,
                    subject = n.Subject,
                    from = n.From,
                    downloadUrl = n.DownloadUrl,
                    receivedAt = n.ReceivedAt,
                    processedAt = n.ProcessedAt,
                    attemptCount = n.AttemptCount,
                })
                .ToListAsync();

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(n => n.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count)
                    .Select(f => new { value = f.Key.ToString(), count = f.Count }),
            };

            // Header counts describe the whole table, not the current filter.
            var all = await db.AsicKeyNotifications.AsNoTracking()
                .Select(n => new { n.Status, n.ReceivedAt, n.ProcessedAt })
                .ToListAsync();

            var now = DateTime.UtcNow;
            var totalCount = all.Count;
            var completedCount = all.Count(n => n.Status == AsicKeyNotificationStatus.Completed);
            var pendingCount = all.Count(n => n.Status == AsicKeyNotificationStatus.Pending);
            var attentionCount = totalCount - completedCount - pendingCount;

            var fourteenDaysAgo = now.Date.AddDays(-13);
            var dailyMap = all
                .Where(n => n.ReceivedAt >= fourteenDaysAgo)
                .GroupBy(n => DateOnly.FromDateTime(n.ReceivedAt))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = all.Count(n => n.ReceivedAt.Date == now.Date);
            var yesterdayCount = all.Count(n => n.ReceivedAt.Date == now.Date.AddDays(-1));
            decimal? deltaPct = yesterdayCount > 0
                ? Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1)
                : null;

            // The scan itself is a Hangfire recurring job; its own bookkeeping is the source of
            // truth for "when did it last run / did it fail / when is the next one".
            var job = ScanJobInfo();

            return Results.Ok(new
            {
                totalCount, completedCount, pendingCount, attentionCount,
                items,
                facets,
                stats = new
                {
                    lastScanAt = job?.LastExecution,
                    lastScanState = job?.LastJobState,
                    lastScanError = job?.Error,
                    nextScanAt = job?.NextExecution,
                    scanCron = job?.Cron,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                },
            });
        });

        // Enqueued rather than run inline: a backlog of PDFs can take minutes, past nginx's
        // proxy timeout. The UI polls /jobs/{id} and refreshes the table as rows land.
        // DisableConcurrentExecution keeps it from overlapping the recurring scan.
        group.MapPost("/scan", (IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<IAsicKeyInboxService>(s => s.ScanAsync(CancellationToken.None));
            return Results.Accepted(value: new { jobId, message = "Scan queued." });
        });

        // Puts every non-completed row back to Pending and queues a scan to work through
        // them — the one-click recovery after fixing the pattern, field id or credentials.
        group.MapPost("/retry-failed", async (ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            var rows = await db.AsicKeyNotifications
                .Where(n => n.Status != AsicKeyNotificationStatus.Completed && n.Status != AsicKeyNotificationStatus.Pending)
                .ToListAsync();
            foreach (var row in rows)
                row.Status = AsicKeyNotificationStatus.Pending;
            await db.SaveChangesAsync();

            var jobId = jobs.Enqueue<IAsicKeyInboxService>(s => s.ScanAsync(CancellationToken.None));
            return Results.Accepted(value: new { jobId, requeued = rows.Count, message = $"{rows.Count} notification(s) queued for retry." });
        });

        group.MapGet("/jobs/{jobId}", (string jobId) =>
        {
            using var connection = JobStorage.Current.GetConnection();
            var job = connection.GetJobData(jobId);
            if (job == null) return Results.NotFound();

            var state = job.State ?? "Unknown";
            var done = state is "Succeeded" or "Failed" or "Deleted";
            var stateData = connection.GetStateData(jobId);

            AsicKeyScanResult? result = null;
            string? error = null;
            if (state == "Succeeded" && stateData?.Data.TryGetValue("Result", out var json) == true && !string.IsNullOrWhiteSpace(json))
            {
                // Same serializer Hangfire used to store it (UseRecommendedSerializerSettings).
                try { result = SerializationHelper.Deserialize<AsicKeyScanResult>(json, SerializationOption.User); }
                catch { /* the row list still tells the story */ }
            }
            if (state == "Failed" && stateData != null)
                error = stateData.Data.TryGetValue("ExceptionMessage", out var message) ? message : stateData.Reason;

            return Results.Ok(new { jobId, state, done, result, error });
        });

        group.MapPost("/{id:guid}/retry", async (Guid id, IAsicKeyInboxService service, CancellationToken ct) =>
        {
            try
            {
                var row = await service.ProcessAsync(id, ct);
                return row == null ? Results.NotFound() : Results.Ok(ToDetail(row));
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        group.MapPost("/{id:guid}/apply", async (Guid id, ApplyRequest body, IAsicKeyInboxService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ContactId))
                return Results.Problem(detail: "contactId is required.", statusCode: 400);
            try
            {
                var row = await service.ApplyToContactAsync(id, body.ContactId, ct);
                return row == null ? Results.NotFound() : Results.Ok(ToDetail(row));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }

    public sealed record ApplyRequest(string ContactId);

    private static object ToDetail(AsicKeyNotification n) => new
    {
        id = n.Id,
        businessName = n.BusinessName,
        abn = n.Abn,
        asicKey = n.AsicKey,
        ontraportContactIds = n.OntraportContactIds,
        ontraportContactsUpdated = n.OntraportContactsUpdated,
        status = n.Status.ToString(),
        errorMessage = n.ErrorMessage,
        processedAt = n.ProcessedAt,
        attemptCount = n.AttemptCount,
    };

    private static RecurringJobDto? ScanJobInfo()
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
