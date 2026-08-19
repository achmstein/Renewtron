using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class DashboardModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/dashboard", async (ApplicationDbContext db, IOptionsSnapshot<PricingSettings> pricing) =>
        {
            var now = DateTime.UtcNow;
            // The business runs on Sydney time — "this month" means the AEST month.
            var aest = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
            var aestNow = TimeZoneInfo.ConvertTimeFromUtc(now, aest);
            var monthStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(aestNow.Year, aestNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), aest);
            var thirtyDaysAgo = now.AddDays(-30);
            var ninetyDaysAgo = now.AddDays(-90);
            var fortyEightHoursAgo = now.AddHours(-48);
            var thirtyDaysAhead = now.AddDays(30);
            var elevenMonthsAgo = now.AddMonths(-11);
            var thirteenMonthsAgo = now.AddMonths(-13);

            var totalSearches = await db.SearchLogs.AsNoTracking().CountAsync();
            var successfulSearches = await db.SearchLogs.AsNoTracking().CountAsync(s => s.Success);
            var renewalsInitiated = await db.RenewalRequests.AsNoTracking().CountAsync();
            var renewalsCompleted = await db.RenewalRequests.AsNoTracking().CountAsync(r => r.Status == RenewalStatus.Completed);
            var renewalsFailed = await db.RenewalRequests.AsNoTracking().CountAsync(r => r.Status == RenewalStatus.Failed);
            var renewalsPending = await db.RenewalRequests.AsNoTracking().CountAsync(r => r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing);

            var totalLeads = await db.Leads.AsNoTracking().CountAsync();
            var convertedLeads = await db.Leads.AsNoTracking().CountAsync(l => l.Outcome == LeadOutcome.RenewalCompleted);
            var notDueLeads = await db.Leads.AsNoTracking().CountAsync(l => l.Outcome == LeadOutcome.NotDueForRenewal);

            // Channel breakdown for the current month — COMPLETED renewals only. Counting
            // failed/pending rows as revenue made this page disagree with the Renewals page.
            var renewtronDirect = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Source == RenewalSource.Renewtron && r.Status == RenewalStatus.Completed
                    && r.CompletedAt != null && r.CompletedAt >= monthStart)
                .Select(r => r.Amount)
                .ToListAsync();
            var ontraport = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Source == RenewalSource.Ontraport && r.Status == RenewalStatus.Completed
                    && r.CompletedAt != null && r.CompletedAt >= monthStart)
                .Select(r => r.Amount)
                .ToListAsync();

            // Average basket — recent completions (90d), falling back to the configured
            // 1-year price rather than a hardcoded number that matches no real fee.
            var basketStats = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Status == RenewalStatus.Completed && r.Amount > 0 && r.CompletedAt >= ninetyDaysAgo)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Amount) })
                .FirstOrDefaultAsync();
            var avgBasket = basketStats != null && basketStats.Count > 0
                ? basketStats.Sum / basketStats.Count
                : pricing.Value.GetCustomerPrice(1);

            // Action 1: leads that found a renewable name and never paid
            var abandonedAtPaymentCount = await db.Leads.AsNoTracking()
                .CountAsync(l => l.Outcome == LeadOutcome.RenewalAvailable
                    && !l.ConvertedToRenewal
                    && l.CreatedAt >= ninetyDaysAgo);

            // Action 2: Ontraport-paid sales waiting on the renewal window in next 30 days
            var ontraportPipeline = await db.OntraportSales.AsNoTracking()
                .Where(s => s.RenewalRequestId == null
                    && s.RenewalDueDate != null
                    && s.RenewalDueDate <= thirtyDaysAhead
                    && s.RenewalDueDate >= now.AddDays(-30))
                .Select(s => s.AmountPaid)
                .ToListAsync();
            var ontraportPipelineCount = ontraportPipeline.Count;
            var ontraportPipelineValue = ontraportPipeline.Sum();

            // Action 3: failed renewals worth retrying (last 30 days)
            var failedRenewals = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Status == RenewalStatus.Failed && r.InitiatedAt >= thirtyDaysAgo)
                .Select(r => r.Amount)
                .ToListAsync();
            var failedRenewalCount = failedRenewals.Count;
            var failedRenewalValue = failedRenewals.Sum();

            // Action 5: 1-year renewals from 11-13 months ago — coming back due soon.
            // Bucketed by CompletedAt: the term starts when the renewal actually completed,
            // not when the customer first clicked (a retried renewal can differ by days).
            var pastCustomersDueSoonCount = await db.RenewalRequests.AsNoTracking()
                .CountAsync(r => r.Status == RenewalStatus.Completed
                    && r.RenewalYears == 1
                    && r.CompletedAt != null
                    && r.CompletedAt >= thirteenMonthsAgo
                    && r.CompletedAt <= elevenMonthsAgo);

            // Recent activity feed — combined timeline (last 48h, capped at 30)
            var paidActivity = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null && r.CompletedAt >= fortyEightHoursAgo)
                .OrderByDescending(r => r.CompletedAt)
                .Take(30)
                .Select(r => new ActivityItem(
                    "paid",
                    r.CompletedAt!.Value,
                    (r.Lead != null ? r.Lead.FullName : r.Email) ?? "—",
                    r.SearchResult != null ? r.SearchResult.BusinessName : null,
                    r.Amount,
                    r.Source.ToString()))
                .ToListAsync();

            var leadActivity = await db.Leads.AsNoTracking()
                .Where(l => l.Outcome == LeadOutcome.RenewalAvailable
                    && !l.ConvertedToRenewal
                    && l.CreatedAt >= fortyEightHoursAgo)
                .OrderByDescending(l => l.CreatedAt)
                .Take(30)
                .Select(l => new ActivityItem(
                    "lead-warm",
                    l.CreatedAt,
                    l.FullName,
                    "name found · no payment",
                    null,
                    null))
                .ToListAsync();

            var activity = paidActivity
                .Concat(leadActivity)
                .OrderByDescending(a => a.At)
                .Take(20)
                .ToList();

            var recentSearches = await db.SearchLogs.AsNoTracking()
                .OrderByDescending(s => s.SearchedAt)
                .Take(10)
                .Select(s => new { s.Id, s.Abn, s.SearchedAt, s.Success, s.ResultsCount })
                .ToListAsync();

            var recentRenewals = await db.RenewalRequests.AsNoTracking()
                .Include(r => r.SearchResult)
                .OrderByDescending(r => r.InitiatedAt)
                .Take(10)
                .Select(r => new
                {
                    r.Id,
                    BusinessName = r.SearchResult != null ? r.SearchResult.BusinessName : null,
                    r.InitiatedAt,
                    Status = r.Status.ToString(),
                })
                .ToListAsync();

            // System health — "is the machine actually running?" at a glance.
            var staleCutoff = now.AddHours(-2);
            var stuckAgg = await db.RenewalRequests.AsNoTracking()
                .Where(r => (r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing)
                    && (r.LastAttemptAt ?? r.InitiatedAt) < staleCutoff)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Oldest = g.Min(r => r.InitiatedAt) })
                .FirstOrDefaultAsync();
            var outboxPending = await db.OntraportSyncOutbox.CountAsync(o => o.SentAt == null && o.AttemptCount < 10);
            var outboxDead = await db.OntraportSyncOutbox.CountAsync(o => o.SentAt == null && o.AttemptCount >= 10);

            object? recurringJobs = null;
            long queueDepth = 0;
            try
            {
                var monitoring = Hangfire.JobStorage.Current.GetMonitoringApi();
                queueDepth = monitoring.Queues().Sum(q => (long)q.Length);
                using var connection = Hangfire.JobStorage.Current.GetConnection();
                recurringJobs = Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection)
                    .Select(j => new { id = j.Id, lastExecution = j.LastExecution, nextExecution = j.NextExecution })
                    .OrderBy(j => j.id)
                    .ToList();
            }
            catch
            {
                // Hangfire storage being unreachable is itself a health signal; the nulls say so.
            }

            var health = new
            {
                stuckCount = stuckAgg?.Count ?? 0,
                oldestStuckAt = stuckAgg?.Oldest,
                outboxPending,
                outboxDead,
                queueDepth,
                recurringJobs,
            };

            return Results.Ok(new
            {
                stats = new
                {
                    totalSearches,
                    successfulSearches,
                    renewalsInitiated,
                    renewalsCompleted,
                    renewalsPending,
                    renewalsFailed,
                    totalLeads,
                    convertedLeads,
                    notDueLeads,
                    renewtronDirectCount = renewtronDirect.Count,
                    renewtronDirectRevenue = renewtronDirect.Sum(),
                    ontraportCount = ontraport.Count,
                    ontraportRevenue = ontraport.Sum(),
                    avgBasket,
                    abandonedAtPaymentCount,
                    abandonedAtPaymentValue = abandonedAtPaymentCount * avgBasket,
                    ontraportPipelineCount,
                    ontraportPipelineValue,
                    failedRenewalCount,
                    failedRenewalValue,
                    pastCustomersDueSoonCount,
                    pastCustomersDueSoonValue = pastCustomersDueSoonCount * avgBasket,
                },
                activity,
                recentSearches,
                recentRenewals,
                health,
            });
        }).RequireAuthorization().WithTags("Admin.Dashboard");
    }

    private sealed record ActivityItem(string Kind, DateTime At, string? Label, string? Detail, decimal? Amount, string? Source);
}
