using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>Filters for the admin renewals grid (comma-separated values for multi-select facets).</summary>
public sealed record RenewalListQuery(
    string? Abn = null,
    string? Status = null,
    string? InitiatedBy = null,
    string? Source = null,
    string? FailedAtStep = null,
    string? ErrorCategory = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? View = null,
    int Page = 1,
    int PageSize = 10);

/// <summary>One page of the grid plus the option counts for each facet.</summary>
public sealed record RenewalListPage(int TotalCount, int Page, int PageSize, IReadOnlyList<object> Items, object Facets);

/// <summary>The admin renewals grid and its KPI strip: read-only queries, kept out of the endpoint.</summary>
public static class RenewalAdminQueries
{
    public static async Task<RenewalListPage> ListAsync(ApplicationDbContext db, RenewalListQuery filter)
    {
        var (abn, status, initiatedBy, source, failedAtStep, errorCategory, dateFrom, dateTo, view, page, pageSize) = filter;
        page = Math.Max(1, page);
        // view=toFix: the "Renewals to fix" queue, same test as the sidebar badge.
        var toFix = string.Equals(view, "toFix", StringComparison.OrdinalIgnoreCase);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Facet params accept comma-separated multi-values ("Failed,Pending").
        var statusValues = Renewtron.Modules.Helpers.ParseEnumList<RenewalStatus>(status);
        var sourceValues = Renewtron.Modules.Helpers.ParseEnumList<RenewalSource>(source);
        var initiatedByValues = Renewtron.Modules.Helpers.ParseEnumList<SearchInitiator>(initiatedBy);
        var categoryValues = (errorCategory ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // One filter pipeline, with each facet dimension excludable so its own option
        // counts can be computed under the OTHER selections (classic faceted search).
        IQueryable<RenewalRequest> Filtered(bool applyStatus = true, bool applySource = true, bool applyInitiatedBy = true, bool applyCategory = true)
        {
            IQueryable<RenewalRequest> q = db.RenewalRequests.AsNoTracking();
            if (toFix)
                q = q.Where(r => !r.IsTest).Where(WorkQueues.RenewalNeedsPerson);
            if (!string.IsNullOrWhiteSpace(abn))
                q = q.Where(r => r.SearchResult.SearchLog.Abn.Contains(abn));
            if (!string.IsNullOrWhiteSpace(failedAtStep))
                q = q.Where(r => r.FailedAtStep == failedAtStep);
            if (dateFrom.HasValue)
                q = q.Where(r => r.InitiatedAt >= dateFrom.Value);
            if (dateTo.HasValue)
            {
                var dt = dateTo.Value.AddDays(1);
                q = q.Where(r => r.InitiatedAt < dt);
            }
            if (applyStatus && statusValues.Length > 0)
                q = q.Where(r => statusValues.Contains(r.Status));
            if (applySource && sourceValues.Length > 0)
                q = q.Where(r => sourceValues.Contains(r.Source));
            if (applyInitiatedBy && initiatedByValues.Length > 0)
                q = q.Where(r => initiatedByValues.Contains(r.SearchResult.SearchLog.InitiatedBy));
            if (applyCategory && categoryValues.Length > 0)
                q = q.Where(r => r.ErrorCategory != null && categoryValues.Contains(r.ErrorCategory));
            return q;
        }

        var query = Filtered()
            .Include(r => r.SearchResult).ThenInclude(s => s.SearchLog)
            .Include(r => r.StripePayment)
            .Include(r => r.Lead);

        var statusFacet = await Filtered(applyStatus: false)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var sourceFacet = await Filtered(applySource: false)
            .GroupBy(r => r.Source).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var initiatedByFacet = await Filtered(applyInitiatedBy: false)
            .GroupBy(r => r.SearchResult.SearchLog.InitiatedBy).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var categoryFacet = await Filtered(applyCategory: false)
            .Where(r => r.ErrorCategory != null)
            .GroupBy(r => r.ErrorCategory).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        var facets = new
        {
            status = statusFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
            source = sourceFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
            initiatedBy = initiatedByFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
            errorCategory = categoryFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key!, count = f.Count }),
        };

        var totalCount = await query.CountAsync();
        var now = DateTime.UtcNow;

        var rows = await query
            .OrderByDescending(r => r.InitiatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                id = r.Id,
                abn = r.SearchResult.SearchLog.Abn,
                businessName = r.SearchResult.BusinessName,
                renewalYears = r.RenewalYears,
                amount = r.Amount,
                status = r.Status.ToString(),
                source = r.Source.ToString(),
                isTest = r.IsTest,
                paymentType = r.PaymentType.ToString(),
                initiatedAt = r.InitiatedAt,
                completedAt = r.CompletedAt,
                email = r.Email,
                errorMessage = r.ErrorMessage,
                failedAtStep = r.FailedAtStep,
                errorCategory = r.ErrorCategory,
                attemptCount = r.AttemptCount,
                nextRetryAt = r.NextRetryAt,
                handledAt = r.HandledAt,
                transactionReference = r.TransactionReference,
                bpayReference = r.BpayReference,
                bpayPaidAt = r.BpayPaidAt,
                initiatedByLabel = r.SearchResult.SearchLog.InitiatedBy.ToString(),
                stripePaymentSucceeded = r.StripePayment != null && r.StripePayment.PaymentStatus == StripePaymentStatuses.Succeeded,
                cardBrand = r.StripePayment != null ? r.StripePayment.CardBrand : null,
                cardLast4 = r.StripePayment != null ? r.StripePayment.CardLast4 : null,
                lead = r.Lead == null ? null : new { id = r.Lead.Id, fullName = r.Lead.FullName, email = r.Lead.Email },
            })
            .ToListAsync();

        // Time the row has been in its CURRENT status (TimeSpan ops aren't translatable in EF,
        // so compute in memory). A terminal row entered its status at CompletedAt; an in-flight
        // row (or a failed one, which has no CompletedAt) has been in status since it was initiated.
        var items = rows.Select(r => new
        {
            r.id, r.abn, r.businessName, r.renewalYears, r.amount, r.status, r.source, r.isTest,
            r.paymentType, r.initiatedAt, r.completedAt, r.email, r.errorMessage, r.failedAtStep,
            r.errorCategory, r.attemptCount, r.nextRetryAt,
            r.transactionReference, r.bpayReference, r.bpayPaidAt, r.initiatedByLabel, r.stripePaymentSucceeded,
            r.cardBrand, r.cardLast4, r.lead,
            timeInStatusHours = r.completedAt.HasValue
                ? Math.Round((now - r.completedAt.Value).TotalHours, 1)
                : Math.Round((now - r.initiatedAt).TotalHours, 1),
        }).ToList();

        return new RenewalListPage(totalCount, page, pageSize, items.Cast<object>().ToList(), facets);
    }

    /// <summary>
    /// Deliberately computed on the UNFILTERED table: these read as global KPIs, and when they
    /// silently tracked the grid filter a status=Failed filter made the success rate 0% and
    /// revenue $0. Day/month boundaries are AEST — the business (and every recurring job) runs
    /// on Sydney time, not UTC.
    /// </summary>
    public static async Task<object> StatsAsync(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        var all = db.RenewalRequests.AsNoTracking().Where(r => !r.IsTest); // test customers aren't revenue
        var aest = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
        var aestNow = TimeZoneInfo.ConvertTimeFromUtc(now, aest);
        var todayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(aestNow.Date, DateTimeKind.Unspecified), aest);
        var yesterdayStart = todayStart.AddDays(-1);
        var fourteenDaysAgo = todayStart.AddDays(-13);
        var monthStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(aestNow.Year, aestNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), aest);
        var thirtyDaysAgo = now.AddDays(-30);
        var staleCutoff = now.AddHours(-2);

        // Success rate over DECIDED outcomes: completed vs failures that are actually
        // final. Timing states (not-due-yet, in-progress recheck, scheduled auto-retry)
        // aren't decisions and used to drag the rate to a meaningless 41%.
        var thirty = await all
            .Where(r => r.InitiatedAt >= thirtyDaysAgo)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Completed = g.Count(r => r.Status == RenewalStatus.Completed),
                RealFailed = g.Count(r => r.Status == RenewalStatus.Failed
                    && r.NextRetryAt == null
                    && r.ErrorCategory != RenewalErrorCategories.NotDueYet
                    && r.ErrorCategory != RenewalErrorCategories.AlreadyInProgress),
            })
            .FirstOrDefaultAsync();
        var total30d = thirty?.Total ?? 0;
        var completed30d = thirty?.Completed ?? 0;
        var decided30d = completed30d + (thirty?.RealFailed ?? 0);
        var successRate30d = decided30d > 0 ? Math.Round((decimal)completed30d * 100m / decided30d, 1) : 0m;

        // Avg completion minutes — the 100 most recent completions; a retried renewal
        // measures from its last attempt, not the original initiation days earlier.
        var completionSamples = await all
            .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null && r.CompletedAt >= thirtyDaysAgo)
            .OrderByDescending(r => r.CompletedAt)
            .Select(r => new { r.InitiatedAt, r.LastAttemptAt, CompletedAt = r.CompletedAt!.Value })
            .Take(100)
            .ToListAsync();
        decimal? avgCompletionMinutes = null;
        if (completionSamples.Count > 0)
        {
            var totalMinutes = completionSamples.Sum(s => (s.CompletedAt - (s.LastAttemptAt ?? s.InitiatedAt)).TotalMinutes);
            avgCompletionMinutes = (decimal)Math.Round(totalMinutes / completionSamples.Count, 1);
        }

        var revenueMtd = await all
            .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null && r.CompletedAt >= monthStart)
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;

        // Honest pipeline split. "In flight" is only what actually has a live run;
        // stale rows are surfaced as stuck (the reconciliation sweep repairs them),
        // scheduled retries carry their NextRetryAt, and needs-review is the human queue.
        var pipelineRows = await all
            .Where(r => r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing || r.Status == RenewalStatus.Failed)
            .Select(r => new { r.Status, r.Amount, r.InitiatedAt, r.LastAttemptAt, r.NextRetryAt, r.ErrorCategory, r.HandledAt })
            .ToListAsync();
        var inflight = pipelineRows.Where(r => r.Status != RenewalStatus.Failed).ToList();
        var liveValue = inflight.Where(r => (r.LastAttemptAt ?? r.InitiatedAt) >= staleCutoff).Sum(r => r.Amount);
        var stuckRows = inflight.Where(r => r.HandledAt == null && (r.LastAttemptAt ?? r.InitiatedAt) < staleCutoff).ToList();
        var stuckValue = stuckRows.Sum(r => r.Amount);
        var stuckCount = stuckRows.Count;
        var scheduledRetryValue = pipelineRows
            .Where(r => r.Status == RenewalStatus.Failed && r.NextRetryAt != null)
            .Sum(r => r.Amount);
        // Close to WorkQueues.RenewalNeedsPerson (the "Renewals to fix" queue), which also requires the client paid.
        var needsReviewRows = pipelineRows
            .Where(r => r.Status == RenewalStatus.Failed && r.HandledAt == null && r.NextRetryAt == null && r.ErrorCategory != RenewalErrorCategories.NotDueYet)
            .ToList();
        var needsReviewCount = needsReviewRows.Count;
        var needsReviewValue = needsReviewRows.Sum(r => r.Amount);

        var failedValue30d = await all
            .Where(r => r.Status == RenewalStatus.Failed && r.InitiatedAt >= thirtyDaysAgo)
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;

        var todayCount = await all.CountAsync(r => r.InitiatedAt >= todayStart);
        var yesterdayCount = await all.CountAsync(r => r.InitiatedAt >= yesterdayStart && r.InitiatedAt < todayStart);
        decimal? deltaPct = null;
        if (yesterdayCount > 0)
            deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

        // AEST day buckets (the timezone shift isn't EF-translatable; bucket in memory).
        var dailyTimes = await all
            .Where(r => r.InitiatedAt >= fourteenDaysAgo)
            .Select(r => r.InitiatedAt)
            .ToListAsync();
        var dailyMap = dailyTimes
            .GroupBy(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t, aest).Date))
            .ToDictionary(g => g.Key, g => g.Count());
        var daily14d = Enumerable.Range(0, 14).Select(i =>
        {
            var date = DateOnly.FromDateTime(aestNow.Date.AddDays(-13 + i));
            return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
        }).ToList();

        // Failure breakdown by category (typed, actionable) — retryable categories are
        // the machine's problem, the rest are the operator's.
        var categoryBreakdown = await all
            .Where(r => r.Status == RenewalStatus.Failed && r.InitiatedAt >= thirtyDaysAgo && r.ErrorCategory != null)
            .GroupBy(r => r.ErrorCategory)
            .Select(g => new { Category = g.Key!, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();

        return new
        {
            successRate30d,
            completed30d,
            decided30d,
            total30d,
            avgCompletionMinutes,
            revenueMtd,
            liveValue,
            stuckValue,
            stuckCount,
            scheduledRetryValue,
            needsReviewCount,
            needsReviewValue,
            failedValue30d,
            today = todayCount,
            yesterday = yesterdayCount,
            deltaPct,
            daily14d,
            errorCategoryBreakdown = categoryBreakdown.Select(c => new
            {
                category = c.Category,
                count = c.Count,
                retryable = c.Category == RenewalErrorCategories.Transient || c.Category == RenewalErrorCategories.AlreadyInProgress,
            }).ToList(),
        };
    }
}
