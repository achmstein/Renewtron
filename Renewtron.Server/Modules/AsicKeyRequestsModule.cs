using Carter;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>
/// Admin view of the ASIC key requests: what needs a person to send it, what has been sent
/// and is waiting for the key email, what came back. The same endpoints feed the desktop
/// tool's "To do from Renewtron" list.
/// </summary>
public sealed class AsicKeyRequestsModule : ICarterModule
{
    public const string RecurringJobId = "asic-key-request-run";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/asic-key-requests").RequireAuthorization().WithTags("Admin.AsicKeyRequests");

        group.MapGet("/", async (ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings, string? status = null, string? search = null) =>
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

            var current = settings.CurrentValue;
            var items = (await Filtered()
                .OrderByDescending(r => r.CreatedAt)
                .Take(500)
                .ToListAsync())
                .Select(r => ToDetail(r, current))
                .ToList();

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count)
                    .Select(f => new { value = f.Key.ToString(), count = f.Count }),
            };

            var all = await db.AsicKeyRequests.AsNoTracking()
                .Select(r => new { r.Status, r.CreatedAt, r.SubmittedAt })
                .ToListAsync();

            var now = DateTime.UtcNow;
            var totalCount = all.Count;
            var pendingCount = all.Count(r => r.Status == AsicKeyRequestStatus.Pending);
            var manualCount = all.Count(r => r.Status == AsicKeyRequestStatus.Manual);
            var submittedCount = all.Count(r => r.Status == AsicKeyRequestStatus.Submitted);
            var keyReceivedCount = all.Count(r => r.Status == AsicKeyRequestStatus.KeyReceived);
            var failedCount = all.Count(r => r.Status == AsicKeyRequestStatus.Failed);

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
                totalCount, pendingCount, manualCount, submittedCount, keyReceivedCount, failedCount,
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

        // ---- the person's list -----------------------------------------------------------
        // What needs doing by hand, with every value for ASIC's form: rows already taken
        // first, then failed, then pending. The desktop tool reads this too.
        group.MapGet("/manual", async (ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings, int max = 50) =>
        {
            var current = settings.CurrentValue;
            var rows = (await db.AsicKeyRequests.AsNoTracking()
                .Where(r => r.Status == AsicKeyRequestStatus.Pending || r.Status == AsicKeyRequestStatus.Failed || r.Status == AsicKeyRequestStatus.Manual)
                .OrderBy(r => r.Status == AsicKeyRequestStatus.Manual ? 0 : r.Status == AsicKeyRequestStatus.Failed ? 1 : 2)
                .ThenBy(r => r.CreatedAt)
                .Take(Math.Clamp(max, 1, 200))
                .ToListAsync())
                .Select(r => ToDetail(r, current))
                .ToList();
            return Results.Ok(rows);
        });

        // Marks a row as taken by a person, so the list shows who has it.
        group.MapPost("/{id:guid}/claim", async (Guid id, ClaimRequest body, ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings) =>
        {
            var row = await db.AsicKeyRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return Results.NotFound();
            if (row.Status is AsicKeyRequestStatus.Submitted or AsicKeyRequestStatus.KeyReceived)
                return Results.Problem(detail: $"Request is already {row.Status}.", statusCode: 409);
            row.Status = AsicKeyRequestStatus.Manual;
            row.ProcessedAt = DateTime.UtcNow;
            row.ErrorMessage = $"Being handled by {Truncate(string.IsNullOrWhiteSpace(body.By) ? "a person" : body.By, 60)}";
            await db.SaveChangesAsync();
            return Results.Ok(ToDetail(row, settings.CurrentValue));
        });

        // What ASIC said when the form was filled in by hand: the reference number from the
        // receipt, or (blank) that it wasn't sent, with a note either way.
        group.MapPost("/{id:guid}/outcome", async (Guid id, OutcomeRequest body, ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings) =>
        {
            var row = await db.AsicKeyRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return Results.NotFound();
            row.AttemptCount++;
            row.ProcessedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(body.ReferenceNumber))
            {
                row.Status = AsicKeyRequestStatus.Submitted;
                row.AsicReferenceNumber = Truncate(body.ReferenceNumber.Trim(), 50);
                row.SubmittedAt = DateTime.UtcNow;
                row.ErrorMessage = string.IsNullOrWhiteSpace(body.Note) ? null : Truncate(body.Note, 1000);
            }
            else
            {
                row.Status = AsicKeyRequestStatus.Failed;
                row.ErrorMessage = Truncate(string.IsNullOrWhiteSpace(body.Note) ? "Not sent" : body.Note, 1000);
                row.CanAutoRetry = false;
            }
            await db.SaveChangesAsync();
            return Results.Ok(ToDetail(row, settings.CurrentValue));
        });

        // Puts a row back in the queue (a failed one, or one a person took and gave up on).
        group.MapPost("/{id:guid}/requeue", async (Guid id, ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings) =>
        {
            var row = await db.AsicKeyRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return Results.NotFound();
            if (row.Status is AsicKeyRequestStatus.Failed or AsicKeyRequestStatus.Manual)
            {
                row.Status = AsicKeyRequestStatus.Pending;
                row.ErrorMessage = null;
                row.CanAutoRetry = true;
            }
            await db.SaveChangesAsync();
            return Results.Ok(ToDetail(row, settings.CurrentValue));
        });

        // Queues (or re-opens) the request for a sale.
        group.MapPost("/for-sale/{saleId:guid}", async (Guid saleId, IAsicKeyRequestService service, IOptionsMonitor<AsicKeyRequestSettings> settings, CancellationToken ct) =>
        {
            try
            {
                var row = await service.QueueForSaleAsync(saleId, ct);
                return Results.Ok(new { request = ToDetail(row, settings.CurrentValue) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 400);
            }
        });

        // Runs the housekeeping pass now (match arrived keys, release stale rows).
        group.MapPost("/run", async (IAsicKeyRequestService service, CancellationToken ct) =>
        {
            var result = await service.ProcessPendingAsync(ct);
            return Results.Ok(result);
        });
    }

    public sealed record ClaimRequest(string? By);
    public sealed record OutcomeRequest(string? ReferenceNumber, string? Note);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>The row plus the form values a person needs, phone split the way ASIC's boxes want it.</summary>
    public static object ToDetail(AsicKeyRequest r, AsicKeyRequestSettings settings)
    {
        var (prefix, number) = AsicKeyRequestService.ResolvePhone(r.Phone, settings);
        return new
        {
            id = r.Id,
            ontraportSaleId = r.OntraportSaleId,
            ontraportContactId = r.OntraportContactId,
            businessName = r.BusinessName,
            abn = r.Abn,
            contactName = (r.GivenNames + " " + r.FamilyName).Trim(),
            givenNames = r.GivenNames,
            familyName = r.FamilyName,
            email = r.Email,
            phone = r.Phone,
            phonePrefix = prefix,
            phoneNumber = number,
            question = string.IsNullOrWhiteSpace(r.Question) ? AsicKeyRequestService.Render(r, settings) : r.Question,
            source = r.Source,
            status = r.Status.ToString(),
            asicReferenceNumber = r.AsicReferenceNumber,
            errorMessage = r.ErrorMessage,
            canAutoRetry = r.CanAutoRetry,
            asicKeyNotificationId = r.AsicKeyNotificationId,
            attemptCount = r.AttemptCount,
            createdAt = r.CreatedAt,
            processedAt = r.ProcessedAt,
            submittedAt = r.SubmittedAt,
            keyReceivedAt = r.KeyReceivedAt,
        };
    }

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
