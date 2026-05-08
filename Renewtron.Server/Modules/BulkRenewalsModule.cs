using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class BulkRenewalsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/bulk-renewals").RequireAuthorization().WithTags("Admin.BulkRenewals");

        group.MapGet("/", async (ApplicationDbContext db) =>
        {
            var uploads = await db.BulkRenewalUploads.AsNoTracking()
                .OrderByDescending(b => b.UploadedAt)
                .Select(b => new
                {
                    id = b.Id,
                    businessName = b.BusinessName,
                    abn = b.Abn,
                    ownerName = b.OwnerName,
                    renewalYears = b.RenewalYears,
                    amount = b.Amount,
                    status = b.Status.ToString(),
                    uploadedAt = b.UploadedAt,
                    renewalDueDate = b.RenewalDueDate,
                    sourceFile = b.SourceFile,
                    errorMessage = b.ErrorMessage,
                })
                .ToListAsync();

            var totalCount = uploads.Count;
            var waitingCount = uploads.Count(u => u.status == nameof(BulkRenewalStatus.WaitingForRenewalWindow));
            var queuedCount = uploads.Count(u => u.status == nameof(BulkRenewalStatus.RenewalQueued));
            var completedCount = uploads.Count(u => u.status == nameof(BulkRenewalStatus.RenewalCompleted));
            var failedCount = uploads.Count(u => u.status == nameof(BulkRenewalStatus.RenewalFailed));

            var now = DateTime.UtcNow;
            var thirtyDaysAhead = now.AddDays(30);
            var pipelineValueNext30d = uploads
                .Where(u => u.renewalDueDate.HasValue && u.renewalDueDate.Value <= thirtyDaysAhead && u.renewalDueDate.Value >= now.AddDays(-30))
                .Where(u => u.status != nameof(BulkRenewalStatus.RenewalCompleted))
                .Sum(u => u.amount);

            // Group by source file (treats null/empty as "(direct)")
            var byBatch = uploads
                .GroupBy(u => string.IsNullOrEmpty(u.sourceFile) ? "(direct)" : u.sourceFile!)
                .Select(g => new
                {
                    sourceFile = g.Key,
                    total = g.Count(),
                    completed = g.Count(x => x.status == nameof(BulkRenewalStatus.RenewalCompleted)),
                    failed = g.Count(x => x.status == nameof(BulkRenewalStatus.RenewalFailed)),
                    pipelineValue = g
                        .Where(x => x.renewalDueDate.HasValue && x.renewalDueDate.Value <= thirtyDaysAhead && x.renewalDueDate.Value >= now.AddDays(-30))
                        .Where(x => x.status != nameof(BulkRenewalStatus.RenewalCompleted))
                        .Sum(x => x.amount),
                    lastUploadAt = g.Max(x => x.uploadedAt),
                })
                .OrderByDescending(b => b.lastUploadAt)
                .Take(10)
                .ToList();

            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var dailyMap = uploads
                .Where(u => u.uploadedAt >= fourteenDaysAgo)
                .GroupBy(u => DateOnly.FromDateTime(u.uploadedAt))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = uploads.Count(u => u.uploadedAt.Date == now.Date);
            var yesterdayCount = uploads.Count(u => u.uploadedAt.Date == now.Date.AddDays(-1));
            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            DateTime? lastUploadAt = uploads.Count > 0 ? uploads.Max(u => u.uploadedAt) : null;

            return Results.Ok(new
            {
                totalCount, waitingCount, queuedCount, completedCount, failedCount,
                items = uploads,
                stats = new
                {
                    pipelineValueNext30d,
                    lastUploadAt,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                    byBatch,
                },
            });
        });

        group.MapPost("/retry-failed", async (ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            // Reset Failed bulk-upload rows back to Waiting so the daily process-eligible
            // job can pick them up again. (No retry-via-Stripe gate here — bulk uploads
            // pay externally so re-queuing the ASIC step is always safe.)
            var failed = await db.BulkRenewalUploads
                .Where(b => b.Status == BulkRenewalStatus.RenewalFailed)
                .ToListAsync();

            foreach (var u in failed)
            {
                u.Status = BulkRenewalStatus.WaitingForRenewalWindow;
                u.ErrorMessage = null;
                u.RenewalRequestId = null;
            }
            await db.SaveChangesAsync();

            jobs.Enqueue<IBulkRenewalService>(s => s.ProcessEligibleRenewalsAsync());
            return Results.Ok(new { retried = failed.Count });
        });

        group.MapPost("/upload", async (HttpRequest req, IBulkRenewalService bulk) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data expected." });

            var form = await req.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "File required." });

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only .xlsx files are supported." });

            using var ms = new MemoryStream();
            await using (var stream = file.OpenReadStream())
                await stream.CopyToAsync(ms);
            ms.Position = 0;

            var result = await bulk.UploadAsync(ms, file.FileName);
            return Results.Ok(result);
        }).DisableAntiforgery();

        group.MapPost("/process-eligible", (IBackgroundJobClient jobs) =>
        {
            var jobId = jobs.Enqueue<IBulkRenewalService>(s => s.ProcessEligibleRenewalsAsync());
            return Results.Accepted(value: new { jobId, message = "Processing job queued. Eligible renewals will be processed in the background. Refresh to see results." });
        });
    }
}
