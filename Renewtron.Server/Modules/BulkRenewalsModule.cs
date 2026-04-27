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

            return Results.Ok(new { totalCount, waitingCount, queuedCount, completedCount, failedCount, items = uploads });
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
