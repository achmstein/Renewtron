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

        group.MapGet("/", async (ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings, string? status = null, string? search = null, int page = 1, int pageSize = 25) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
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
            var filteredCount = await Filtered().CountAsync();
            var items = (await Filtered()
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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

            // Header counts are for the whole table, counted in SQL.
            var byStatus = string.IsNullOrWhiteSpace(search)
                ? statusFacet
                : await db.AsicKeyRequests.AsNoTracking()
                    .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            int CountOf(AsicKeyRequestStatus s) => byStatus.Where(x => x.Key == s).Sum(x => x.Count);
            var totalCount = byStatus.Sum(x => x.Count);
            var pendingCount = CountOf(AsicKeyRequestStatus.Pending);
            var manualCount = CountOf(AsicKeyRequestStatus.Manual);
            var submittedCount = CountOf(AsicKeyRequestStatus.Submitted);
            var keyReceivedCount = CountOf(AsicKeyRequestStatus.KeyReceived);
            var failedCount = CountOf(AsicKeyRequestStatus.Failed);

            var now = DateTime.UtcNow;
            var fourteenDaysAgo = now.Date.AddDays(-13);
            var dailyMap = (await db.AsicKeyRequests.AsNoTracking()
                    .Where(r => r.SubmittedAt != null && r.SubmittedAt >= fourteenDaysAgo)
                    .GroupBy(r => r.SubmittedAt!.Value.Date)
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
            decimal? deltaPct = yesterdayCount > 0
                ? Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1)
                : null;

            var job = RunJobInfo();

            return Results.Ok(new
            {
                totalCount, pendingCount, manualCount, submittedCount, keyReceivedCount, failedCount,
                filteredCount, page, pageSize,
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
        // first, then failed, then pending. The desktop tool reads this too, and submits what it
        // gets, so test customers' rows are never listed here.
        group.MapGet("/manual", async (ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings, int max = 50) =>
        {
            var current = settings.CurrentValue;
            var rows = (await db.AsicKeyRequests.AsNoTracking()
                .Where(r => r.Status == AsicKeyRequestStatus.Pending || r.Status == AsicKeyRequestStatus.Failed || r.Status == AsicKeyRequestStatus.Manual)
                .Where(r => !r.IsTest)
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
                row.Question = AsicKeyRequestService.Render(row, settings.CurrentValue);
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
        // One request as its record page shows it: the request, whose it is, the sale behind it
        // and the key letter that answered it.
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, IOptionsMonitor<AsicKeyRequestSettings> settings) =>
        {
            var r = await db.AsicKeyRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            var client = await ClientRefs.ByIdAsync(db, r.ClientId) ?? await ClientRefs.ByAbnAsync(db, r.Abn);
            var sale = r.OntraportSaleId is { } saleId
                ? await db.OntraportSales.AsNoTracking().Where(s => s.Id == saleId)
                    .Select(s => new { id = s.Id, status = s.Status.ToString(), syncedAt = s.SyncedAt }).FirstOrDefaultAsync()
                : null;
            var letter = r.AsicKeyNotificationId is { } letterId
                ? await db.AsicKeyNotifications.AsNoTracking().Where(n => n.Id == letterId)
                    .Select(n => new { id = n.Id, status = n.Status.ToString(), receivedAt = n.ReceivedAt, hasKey = n.AsicKey != null }).FirstOrDefaultAsync()
                : null;
            return Results.Ok(new { request = ToDetail(r, settings.CurrentValue), client, sale, letter });
        });

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
            otherBusinessNames = r.AllBusinessNames().Skip(1).ToList(),
            abn = r.Abn,
            contactName = (r.GivenNames + " " + r.FamilyName).Trim(),
            givenNames = r.GivenNames,
            familyName = r.FamilyName,
            email = r.Email,
            phone = r.Phone,
            phonePrefix = prefix,
            phoneNumber = number,
            question = Question(r, settings),
            source = r.Source,
            isTest = r.IsTest,
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

    /// <summary>
    /// The enquiry text. Until it's sent it follows the current Settings (key delivery email,
    /// template), so a change there reaches rows already queued; once sent, it's what went out.
    /// </summary>
    private static string Question(AsicKeyRequest r, AsicKeyRequestSettings settings) =>
        (r.Status is AsicKeyRequestStatus.Submitted or AsicKeyRequestStatus.KeyReceived) && !string.IsNullOrWhiteSpace(r.Question)
            ? r.Question
            : AsicKeyRequestService.Render(r, settings);

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
