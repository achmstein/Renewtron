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
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var totalSearches = await db.SearchLogs.AsNoTracking().CountAsync();
            var successfulSearches = await db.SearchLogs.AsNoTracking().CountAsync(s => s.Success);
            var renewalsInitiated = await db.RenewalRequests.AsNoTracking().CountAsync();
            var renewalsCompleted = await db.RenewalRequests.AsNoTracking().CountAsync(r => r.Status == RenewalStatus.Completed);
            var renewalsFailed = await db.RenewalRequests.AsNoTracking().CountAsync(r => r.Status == RenewalStatus.Failed);
            var renewalsPending = await db.RenewalRequests.AsNoTracking().CountAsync(r => r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing);

            var totalLeads = await db.Leads.AsNoTracking().CountAsync();
            var convertedLeads = await db.Leads.AsNoTracking().CountAsync(l => l.Outcome == LeadOutcome.RenewalCompleted);
            var notDueLeads = await db.Leads.AsNoTracking().CountAsync(l => l.Outcome == LeadOutcome.NotDueForRenewal);

            var renewtronDirect = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Source == RenewalSource.Renewtron && r.InitiatedAt >= monthStart)
                .Select(r => new { r.Amount })
                .ToListAsync();
            var ontraport = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Source == RenewalSource.Ontraport && r.InitiatedAt >= monthStart)
                .Select(r => new { r.Amount })
                .ToListAsync();

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
                    renewtronDirectRevenue = renewtronDirect.Sum(r => r.Amount),
                    ontraportCount = ontraport.Count,
                    ontraportRevenue = ontraport.Sum(r => r.Amount),
                },
                recentSearches,
                recentRenewals,
            });
        }).RequireAuthorization().WithTags("Admin.Dashboard");
    }
}
