using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class DashboardModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/dashboard", async (ApplicationDbContext db) =>
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var thirtyDaysAgo = now.AddDays(-30);
            var ninetyDaysAgo = now.AddDays(-90);
            var fortyEightHoursAgo = now.AddHours(-48);
            var twentyFourHoursAgo = now.AddHours(-24);
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

            // Channel breakdown for the current month
            var renewtronDirect = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Source == RenewalSource.Renewtron && r.InitiatedAt >= monthStart)
                .Select(r => r.Amount)
                .ToListAsync();
            var ontraport = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Source == RenewalSource.Ontraport && r.InitiatedAt >= monthStart)
                .Select(r => r.Amount)
                .ToListAsync();

            // Average basket — used to estimate $ value of leads that haven't paid yet
            var basketStats = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Status == RenewalStatus.Completed && r.Amount > 0)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Amount) })
                .FirstOrDefaultAsync();
            var avgBasket = basketStats != null && basketStats.Count > 0 ? basketStats.Sum / basketStats.Count : 79m;

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

            // Action 4: ATO onboarding stuck >24h (renewal completed, ATO not done)
            var stuckAtoCount = await db.RenewalRequests.AsNoTracking()
                .CountAsync(r => r.AtoOnboardingJobId != null
                    && r.AtoOnboardingStatus != "Completed"
                    && r.AtoOnboardingStatus != "Failed"
                    && r.AtoOnboardingCompletedAt == null
                    && r.CompletedAt != null
                    && r.CompletedAt < twentyFourHoursAgo);

            // Action 5: 1-year renewals from 11-13 months ago — coming back due soon
            var pastCustomersDueSoonCount = await db.RenewalRequests.AsNoTracking()
                .CountAsync(r => r.Status == RenewalStatus.Completed
                    && r.RenewalYears == 1
                    && r.InitiatedAt >= thirteenMonthsAgo
                    && r.InitiatedAt <= elevenMonthsAgo);

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

            var atoActivity = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.AtoOnboardingCompletedAt != null
                    && r.AtoOnboardingCompletedAt >= fortyEightHoursAgo
                    && (r.AtoOnboardingStatus == "Completed" || r.AtoOnboardingStatus == "Failed"))
                .OrderByDescending(r => r.AtoOnboardingCompletedAt)
                .Take(30)
                .Select(r => new ActivityItem(
                    r.AtoOnboardingStatus == "Completed" ? "ato-ok" : "ato-fail",
                    r.AtoOnboardingCompletedAt!.Value,
                    (r.Lead != null ? r.Lead.FullName : r.Email) ?? "—",
                    r.SearchResult != null ? r.SearchResult.BusinessName : null,
                    null,
                    null))
                .ToListAsync();

            var activity = paidActivity
                .Concat(leadActivity)
                .Concat(atoActivity)
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
                    stuckAtoCount,
                    pastCustomersDueSoonCount,
                    pastCustomersDueSoonValue = pastCustomersDueSoonCount * avgBasket,
                },
                activity,
                recentSearches,
                recentRenewals,
            });
        }).RequireAuthorization().WithTags("Admin.Dashboard");
    }

    private sealed record ActivityItem(string Kind, DateTime At, string? Label, string? Detail, decimal? Amount, string? Source);
}
