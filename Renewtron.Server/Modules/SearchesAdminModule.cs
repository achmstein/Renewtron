using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class SearchesAdminModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/searches").RequireAuthorization().WithTags("Admin.Searches");

        group.MapGet("/", async (
            ApplicationDbContext db,
            string? abn = null,
            string? success = null,
            string? initiatedBy = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            bool includeSystem = false,
            int page = 1,
            int pageSize = 25) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var skip = (page - 1) * pageSize;

            // System searches are internal plumbing (Ontraport sync, bulk renewal pipelines).
            // They flood the list and dilute customer-funnel metrics, so we exclude them by
            // default. An explicit initiatedBy filter overrides; includeSystem=true also opts in.
            var explicitInitiator = !string.IsNullOrWhiteSpace(initiatedBy)
                && Enum.TryParse<SearchInitiator>(initiatedBy, true, out _);
            var hideSystem = !includeSystem && !explicitInitiator;

            var query = db.SearchLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(abn))
                query = query.Where(s => s.Abn.Contains(abn));
            if (success == "true")
                query = query.Where(s => s.Success);
            else if (success == "false")
                query = query.Where(s => !s.Success);
            if (!string.IsNullOrWhiteSpace(initiatedBy) && Enum.TryParse<SearchInitiator>(initiatedBy, true, out var initEnum))
                query = query.Where(s => s.InitiatedBy == initEnum);
            else if (hideSystem)
                query = query.Where(s => s.InitiatedBy != SearchInitiator.System);
            if (dateFrom.HasValue)
                query = query.Where(s => s.SearchedAt >= dateFrom.Value.ToUniversalTime());
            if (dateTo.HasValue)
            {
                var until = dateTo.Value.Date.AddDays(1).ToUniversalTime();
                query = query.Where(s => s.SearchedAt < until);
            }

            var totalCount = await query.CountAsync();

            // Pull the page first
            var pageItems = await query
                .OrderByDescending(s => s.SearchedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.Abn,
                    s.SessionId,
                    s.SearchedAt,
                    s.Success,
                    s.ErrorMessage,
                    s.ResultsCount,
                    InitiatedBy = s.InitiatedBy.ToString(),
                    s.IpAddress,
                    Results = s.Results.Select(r => new { r.Id, r.BusinessName }).ToList(),
                    Lead = db.Leads
                        .Where(l => l.SearchLogId == s.Id)
                        .Select(l => new { l.Id, l.FullName, l.Email, Outcome = l.Outcome.ToString(), l.ConvertedToRenewal })
                        .FirstOrDefault(),
                    Renewal = db.RenewalRequests
                        .Where(r => r.SearchResult.SearchLogId == s.Id)
                        .OrderByDescending(r => r.InitiatedAt)
                        .Select(r => new { r.Id, r.Amount, Status = r.Status.ToString() })
                        .FirstOrDefault(),
                    // Funnel facts aggregated across ALL of this search's renewal attempts, not just
                    // the latest — a completed name shouldn't read as failed because a later retry failed.
                    HasLead = db.Leads.Any(l => l.SearchLogId == s.Id),
                    AnyPaid = db.RenewalRequests.Any(r => r.SearchResult.SearchLogId == s.Id && r.Status == RenewalStatus.Completed),
                    AnyInflight = db.RenewalRequests.Any(r => r.SearchResult.SearchLogId == s.Id
                        && (r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing)),
                    AnyFailed = db.RenewalRequests.Any(r => r.SearchResult.SearchLogId == s.Id && r.Status == RenewalStatus.Failed),
                })
                .ToListAsync();

            // Repeat-search detection: ABNs searched ≥3 times in last 7 days
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var pageAbns = pageItems.Select(i => i.Abn).Distinct().ToList();
            var repeatAbns = await db.SearchLogs.AsNoTracking()
                .Where(s => s.SearchedAt >= sevenDaysAgo && pageAbns.Contains(s.Abn))
                .GroupBy(s => s.Abn)
                .Where(g => g.Count() >= 3)
                .Select(g => new { Abn = g.Key, Count = g.Count() })
                .ToListAsync();
            var repeatMap = repeatAbns.ToDictionary(r => r.Abn, r => r.Count);

            var items = pageItems.Select(s => new
            {
                id = s.Id,
                abn = s.Abn,
                sessionId = s.SessionId,
                searchedAt = s.SearchedAt,
                success = s.Success,
                errorMessage = s.ErrorMessage,
                resultsCount = s.ResultsCount,
                initiatedBy = s.InitiatedBy,
                ipAddress = s.IpAddress,
                hasRenewal = s.Renewal != null,
                firstBusinessName = s.Results.FirstOrDefault()?.BusinessName,
                lead = s.Lead == null ? null : new
                {
                    id = s.Lead.Id,
                    fullName = s.Lead.FullName,
                    email = s.Lead.Email,
                    outcome = s.Lead.Outcome,
                    converted = s.Lead.ConvertedToRenewal,
                },
                renewal = s.Renewal == null ? null : new
                {
                    id = s.Renewal.Id,
                    amount = s.Renewal.Amount,
                    status = s.Renewal.Status,
                },
                funnel = new
                {
                    hasLead = s.HasLead,
                    anyPaid = s.AnyPaid,
                    anyInflight = s.AnyInflight,
                    anyFailed = s.AnyFailed,
                },
                repeatCount7d = repeatMap.GetValueOrDefault(s.Abn, 0),
            }).ToList();

            // Stats block — computed against the same population as the list (so success
            // rate / conversion rate reflect what the admin is actually looking at). System
            // searches are excluded by default for the same reason as the list filter above.
            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime(); // include today + 13 prior = 14 buckets
            var todayStart = now.Date.ToUniversalTime();
            var yesterdayStart = todayStart.AddDays(-1);

            var statsBase = db.SearchLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(initiatedBy) && Enum.TryParse<SearchInitiator>(initiatedBy, true, out var statsInit))
                statsBase = statsBase.Where(s => s.InitiatedBy == statsInit);
            else if (hideSystem)
                statsBase = statsBase.Where(s => s.InitiatedBy != SearchInitiator.System);

            var totalAllTime = await statsBase.CountAsync();

            var thirty = await statsBase
                .Where(s => s.SearchedAt >= thirtyDaysAgo)
                .GroupBy(s => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Success = g.Count(x => x.Success),
                })
                .FirstOrDefaultAsync();

            // Conversion rate (30d): searches whose lead progressed to a Completed renewal
            var conversions30d = await statsBase
                .Where(s => s.SearchedAt >= thirtyDaysAgo)
                .CountAsync(s => db.RenewalRequests.Any(r =>
                    r.SearchResult.SearchLogId == s.Id && r.Status == RenewalStatus.Completed));

            var todayCount = await statsBase
                .CountAsync(s => s.SearchedAt >= todayStart);
            var yesterdayCount = await statsBase
                .CountAsync(s => s.SearchedAt >= yesterdayStart && s.SearchedAt < todayStart);

            // Daily volume for last 14 days — group in DB, fill gaps in code
            var dailyRaw = await statsBase
                .Where(s => s.SearchedAt >= fourteenDaysAgo)
                .GroupBy(s => new { s.SearchedAt.Year, s.SearchedAt.Month, s.SearchedAt.Day })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
                .ToListAsync();
            var dailyMap = dailyRaw.ToDictionary(d => new DateOnly(d.Year, d.Month, d.Day), d => d.Count);
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();

            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);
            else if (todayCount > 0)
                deltaPct = null; // no comparable baseline

            var total30 = thirty?.Total ?? 0;
            var success30 = thirty?.Success ?? 0;
            var successRate30d = total30 > 0 ? Math.Round((decimal)success30 * 100m / total30, 1) : 0m;
            var conversionRate30d = total30 > 0 ? Math.Round((decimal)conversions30d * 100m / total30, 1) : 0m;

            return Results.Ok(new
            {
                totalCount,
                page,
                pageSize,
                items,
                stats = new
                {
                    totalAllTime,
                    total30d = total30,
                    success30d = success30,
                    successRate30d,
                    conversions30d,
                    conversionRate30d,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                },
            });
        });

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var s = await db.SearchLogs.AsNoTracking()
                .Include(x => x.Results)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();

            var lead = await db.Leads.AsNoTracking()
                .Where(l => l.SearchLogId == s.Id)
                .Select(l => new { l.Id, l.FullName, l.Email, l.MobileNumber, Outcome = l.Outcome.ToString() })
                .FirstOrDefaultAsync();

            // Latest renewal attempt per result. Pulled flat and grouped in memory to avoid the
            // per-group "OrderBy().First()" pattern EF can't translate.
            var resultIds = s.Results.Select(r => r.Id).ToList();
            var renewalsRaw = await db.RenewalRequests.AsNoTracking()
                .Where(r => resultIds.Contains(r.SearchResultId))
                .Select(r => new { r.SearchResultId, r.Id, Status = r.Status.ToString(), r.InitiatedAt, r.RenewalYears, r.Amount })
                .ToListAsync();
            var latestByResult = renewalsRaw
                .GroupBy(r => r.SearchResultId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.InitiatedAt).First());

            var results = s.Results.Select(r => new
            {
                id = r.Id,
                businessName = r.BusinessName,
                accountNumber = r.AccountNumber,
                registrationDate = r.RegistrationDate,
                renewalRequest = latestByResult.TryGetValue(r.Id, out var rr)
                    ? new { id = rr.Id, status = rr.Status, initiatedAt = rr.InitiatedAt, renewalYears = rr.RenewalYears, amount = rr.Amount }
                    : null,
            }).ToList();

            // Same funnel facts the list returns, aggregated across every renewal attempt.
            var anyPaid = renewalsRaw.Any(r => r.Status == "Completed");
            var anyInflight = renewalsRaw.Any(r => r.Status is "Pending" or "Processing");
            var anyFailed = renewalsRaw.Any(r => r.Status == "Failed");

            return Results.Ok(new
            {
                id = s.Id,
                abn = s.Abn,
                sessionId = s.SessionId,
                searchedAt = s.SearchedAt,
                success = s.Success,
                errorMessage = s.ErrorMessage,
                ipAddress = s.IpAddress,
                userAgent = s.UserAgent,
                resultsCount = s.ResultsCount,
                initiatedBy = s.InitiatedBy.ToString(),
                lead = lead == null ? null : new
                {
                    id = lead.Id,
                    fullName = lead.FullName,
                    email = lead.Email,
                    mobileNumber = lead.MobileNumber,
                    outcome = lead.Outcome,
                },
                results,
                funnel = new { hasLead = lead != null, anyPaid, anyInflight, anyFailed },
            });
        });
    }
}
