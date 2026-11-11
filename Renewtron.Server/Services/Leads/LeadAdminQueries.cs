using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>Filters for the admin leads grid (comma-separated values for multi-select facets).</summary>
public sealed record LeadListQuery(
    string? Outcome = null,
    string? Reminder = null,
    string? Search = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? HasRenewal = null,
    int Take = 100,
    int Page = 1);

/// <summary>The admin leads grid, its facets and its KPI strip (which follows the grid's filter).</summary>
public static class LeadAdminQueries
{
    public static async Task<object> ListAsync(ApplicationDbContext db, LeadListQuery filter)
    {
        var (outcome, reminder, search, dateFrom, dateTo, hasRenewal, take, page) = filter;
        take = Math.Clamp(take, 1, 500);
        page = Math.Max(1, page);

        // Facet params accept comma-separated multi-values ("RenewalAvailable,Pending").
        var outcomeValues = Modules.Helpers.ParseEnumList<LeadOutcome>(outcome);

        // Reminder round-trips as OptedIn/NotOptedIn (facet values); legacy true/false
        // still accepted. Both selected (or neither) means no filter.
        var reminderTokens = (reminder ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var wantOptedIn = reminderTokens.Any(t =>
            t.Equals("OptedIn", StringComparison.OrdinalIgnoreCase) || t.Equals("true", StringComparison.OrdinalIgnoreCase));
        var wantNotOptedIn = reminderTokens.Any(t =>
            t.Equals("NotOptedIn", StringComparison.OrdinalIgnoreCase) || t.Equals("false", StringComparison.OrdinalIgnoreCase));
        bool? reminderOn = wantOptedIn == wantNotOptedIn ? null : wantOptedIn;

        // One filter pipeline, with each facet dimension excludable so its own option
        // counts can be computed under the OTHER selections (classic faceted search).
        IQueryable<Lead> Filtered(bool applyOutcome = true, bool applyReminder = true)
        {
            IQueryable<Lead> q = db.Leads.AsNoTracking();
            if (applyOutcome && outcomeValues.Length > 0)
                q = q.Where(l => outcomeValues.Contains(l.Outcome));
            if (applyReminder && reminderOn is { } on)
                q = q.Where(l => l.ReminderOptIn == on);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                q = q.Where(l =>
                    l.Abn.Contains(term) ||
                    l.FullName.ToLower().Contains(term) ||
                    l.Email.ToLower().Contains(term));
            }
            if (dateFrom.HasValue)
                q = q.Where(l => l.CreatedAt >= dateFrom.Value.ToUniversalTime());
            if (dateTo.HasValue)
            {
                var until = dateTo.Value.Date.AddDays(1).ToUniversalTime();
                q = q.Where(l => l.CreatedAt < until);
            }
            if (hasRenewal == "true")
                q = q.Where(l => l.RenewalRequests.Any());
            else if (hasRenewal == "false")
                q = q.Where(l => !l.RenewalRequests.Any());
            return q;
        }

        var query = Filtered();

        var outcomeFacet = await Filtered(applyOutcome: false)
            .GroupBy(l => l.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var reminderFacet = await Filtered(applyReminder: false)
            .GroupBy(l => l.ReminderOptIn).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        var facets = new
        {
            outcome = outcomeFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
            reminder = reminderFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key ? "OptedIn" : "NotOptedIn", count = f.Count }),
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * take)
            .Take(take)
            .Select(l => new
            {
                id = l.Id,
                abn = l.Abn,
                fullName = l.FullName,
                email = l.Email,
                mobileNumber = l.MobileNumber,
                createdAt = l.CreatedAt,
                source = l.Source,
                outcome = l.Outcome.ToString(),
                progress = l.Progress.ToString(),
                reminderOptIn = l.ReminderOptIn,
                convertedToRenewal = l.ConvertedToRenewal,
                convertedAt = l.ConvertedAt,
                searchLog = l.SearchLog == null ? null : new
                {
                    id = l.SearchLog.Id,
                    searchedAt = l.SearchLog.SearchedAt,
                    success = l.SearchLog.Success,
                    resultsCount = l.SearchLog.ResultsCount,
                },
                firstBusinessName = l.SearchLog == null
                    ? null
                    : l.SearchLog.Results.OrderBy(r => r.Id).Select(r => r.BusinessName).FirstOrDefault(),
                renewal = l.RenewalRequests
                    .OrderByDescending(r => r.InitiatedAt)
                    .Select(r => new { id = r.Id, status = r.Status.ToString(), amount = r.Amount })
                    .FirstOrDefault(),
            })
            .ToListAsync();

        // ─────────── Stats block (computed against the same filter) ───────────
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
        var todayStart = now.Date.ToUniversalTime();
        var yesterdayStart = todayStart.AddDays(-1);

        var totalAllTime = await db.Leads.AsNoTracking().CountAsync();

        var thirty = await query
            .Where(l => l.CreatedAt >= thirtyDaysAgo)
            .GroupBy(l => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Converted = g.Count(l => l.ConvertedToRenewal),
            })
            .FirstOrDefaultAsync();
        var total30d = thirty?.Total ?? 0;
        var converted30d = thirty?.Converted ?? 0;
        var conversionRate30d = total30d > 0 ? Math.Round((decimal)converted30d * 100m / total30d, 1) : 0m;

        // Avg time-to-renewal (Lead.CreatedAt → first Completed RenewalRequest.CompletedAt)
        decimal? avgHoursToConvert = null;
        var convertedSamples = await db.Leads.AsNoTracking()
            .Where(l => l.ConvertedToRenewal
                && l.CreatedAt >= thirtyDaysAgo
                && l.RenewalRequests.Any(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null))
            .Select(l => new
            {
                l.CreatedAt,
                CompletedAt = l.RenewalRequests
                    .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null)
                    .OrderBy(r => r.CompletedAt)
                    .Select(r => r.CompletedAt!.Value)
                    .FirstOrDefault(),
            })
            .Take(500)
            .ToListAsync();
        if (convertedSamples.Count > 0)
        {
            var totalHours = convertedSamples.Sum(s => (s.CompletedAt - s.CreatedAt).TotalHours);
            avgHoursToConvert = (decimal)Math.Round(totalHours / convertedSamples.Count, 1);
        }

        var todayCount = await query.CountAsync(l => l.CreatedAt >= todayStart);
        var yesterdayCount = await query.CountAsync(l => l.CreatedAt >= yesterdayStart && l.CreatedAt < todayStart);

        decimal? deltaPct = null;
        if (yesterdayCount > 0)
            deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

        var dailyRaw = await query
            .Where(l => l.CreatedAt >= fourteenDaysAgo)
            .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month, l.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .ToListAsync();
        var dailyMap = dailyRaw.ToDictionary(d => new DateOnly(d.Year, d.Month, d.Day), d => d.Count);
        var daily14d = Enumerable.Range(0, 14).Select(i =>
        {
            var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
            return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
        }).ToList();

        var outcomeBreakdownRaw = await query
            .GroupBy(l => l.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToListAsync();
        var outcomeBreakdown = outcomeBreakdownRaw.ToDictionary(b => b.Outcome.ToString(), b => b.Count);

        var winBackEligible = await query.CountAsync(l =>
            l.Outcome == LeadOutcome.RenewalAvailable && !l.ConvertedToRenewal && l.Email != null && l.Email != "");

        // Average basket — used to estimate $ recoverable from win-back eligible leads
        var basketStats = await db.RenewalRequests.AsNoTracking()
            .Where(r => r.Status == RenewalStatus.Completed && r.Amount > 0)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Amount) })
            .FirstOrDefaultAsync();
        var avgBasket = basketStats != null && basketStats.Count > 0 ? basketStats.Sum / basketStats.Count : 79m;

        return new
        {
            totalCount,
            page,
            pageSize = take,
            items,
            facets,
            stats = new
            {
                totalAllTime,
                total30d,
                converted30d,
                conversionRate30d,
                avgHoursToConvert,
                today = todayCount,
                yesterday = yesterdayCount,
                deltaPct,
                daily14d,
                outcomeBreakdown,
                winBackEligible,
                winBackRecoverableValue = winBackEligible * avgBasket,
                avgBasket,
            },
        };
    }
}
