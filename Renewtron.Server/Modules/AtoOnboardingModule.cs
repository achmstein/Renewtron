using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Renewtron.Abstractions;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class AtoOnboardingModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/ato-onboarding").RequireAuthorization().WithTags("Admin.AtoOnboarding");

        admin.MapGet("/", async (
            ApplicationDbContext db,
            string? status = null,
            string? search = null,
            int take = 100) =>
        {
            take = Math.Clamp(take, 1, 500);
            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var twentyFourHoursAgo = now.AddHours(-24);

            IQueryable<RenewalRequest> baseQuery = db.RenewalRequests
                .AsNoTracking()
                .Where(r => r.AtoOnboardingJobId != null);

            var totalCount = await baseQuery.CountAsync();
            var pendingCount = await baseQuery.CountAsync(r => r.AtoOnboardingStatus == "Pending" || r.AtoOnboardingStatus == "InProgress" || r.AtoOnboardingStatus == "AwaitingAuth");
            var completedCount = await baseQuery.CountAsync(r => r.AtoOnboardingStatus == "Completed");
            var failedCount = await baseQuery.CountAsync(r => r.AtoOnboardingStatus == "Failed");

            // Filtered query for the table
            IQueryable<RenewalRequest> query = baseQuery;
            if (!string.IsNullOrEmpty(status))
                query = query.Where(r => r.AtoOnboardingStatus == status);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(r =>
                    (r.Email != null && r.Email.ToLower().Contains(term)) ||
                    (r.SearchResult.SearchLog.Abn.Contains(term)) ||
                    (r.AtoOnboardingJobId != null && r.AtoOnboardingJobId.Contains(term)));
            }

            var items = await query
                .Include(r => r.SearchResult)
                    .ThenInclude(sr => sr.SearchLog)
                .Include(r => r.Lead)
                .OrderByDescending(r => r.InitiatedAt)
                .Take(take)
                .Select(r => new
                {
                    renewalRequestId = r.Id,
                    leadId = r.LeadId,
                    fullName = r.Lead != null ? r.Lead.FullName : null,
                    email = r.Email,
                    abn = r.SearchResult.SearchLog.Abn,
                    businessName = r.SearchResult.BusinessName,
                    initiatedAt = r.InitiatedAt,
                    completedAt = r.CompletedAt,
                    renewalStatus = r.Status.ToString(),
                    atoJobId = r.AtoOnboardingJobId,
                    atoStatus = r.AtoOnboardingStatus,
                    atoCompletedAt = r.AtoOnboardingCompletedAt,
                    timeInFlightHours = r.AtoOnboardingCompletedAt != null
                        ? (decimal?)null
                        : (r.CompletedAt != null ? (decimal)(now - r.CompletedAt!.Value).TotalHours : (decimal?)null),
                })
                .ToListAsync();

            // Stats
            var stuck24h = await baseQuery.CountAsync(r =>
                (r.AtoOnboardingStatus == "Pending" || r.AtoOnboardingStatus == "InProgress" || r.AtoOnboardingStatus == "AwaitingAuth")
                && r.AtoOnboardingCompletedAt == null
                && r.CompletedAt != null
                && r.CompletedAt < twentyFourHoursAgo);

            var thirty = await baseQuery
                .Where(r => r.AtoOnboardingCompletedAt != null && r.AtoOnboardingCompletedAt >= thirtyDaysAgo)
                .GroupBy(r => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Completed = g.Count(r => r.AtoOnboardingStatus == "Completed"),
                })
                .FirstOrDefaultAsync();
            var total30d = thirty?.Total ?? 0;
            var completed30d = thirty?.Completed ?? 0;
            var successRate30d = total30d > 0 ? Math.Round((decimal)completed30d * 100m / total30d, 1) : 0m;

            // Avg minutes (Renewal.CompletedAt → Ato.CompletedAt)
            var samples = await baseQuery
                .Where(r => r.AtoOnboardingStatus == "Completed"
                    && r.AtoOnboardingCompletedAt != null
                    && r.CompletedAt != null
                    && r.AtoOnboardingCompletedAt >= thirtyDaysAgo)
                .Select(r => new { Start = r.CompletedAt!.Value, End = r.AtoOnboardingCompletedAt!.Value })
                .Take(500)
                .ToListAsync();
            decimal? avgOnboardingMinutes = null;
            if (samples.Count > 0)
            {
                var totalMinutes = samples.Sum(s => (s.End - s.Start).TotalMinutes);
                avgOnboardingMinutes = (decimal)Math.Round(totalMinutes / samples.Count, 1);
            }

            // Daily completion volume for last 14 days
            var dailyRaw = await baseQuery
                .Where(r => r.AtoOnboardingCompletedAt != null && r.AtoOnboardingCompletedAt >= fourteenDaysAgo)
                .Select(r => r.AtoOnboardingCompletedAt!.Value)
                .ToListAsync();
            var dailyMap = dailyRaw
                .GroupBy(d => DateOnly.FromDateTime(d))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();
            var todayCount = await baseQuery.CountAsync(r =>
                r.AtoOnboardingCompletedAt != null && r.AtoOnboardingCompletedAt >= now.Date.ToUniversalTime());
            var yesterdayCount = await baseQuery.CountAsync(r =>
                r.AtoOnboardingCompletedAt != null
                && r.AtoOnboardingCompletedAt >= now.Date.AddDays(-1).ToUniversalTime()
                && r.AtoOnboardingCompletedAt < now.Date.ToUniversalTime());
            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            return Results.Ok(new
            {
                totalCount, pendingCount, completedCount, failedCount,
                items,
                stats = new
                {
                    successRate30d,
                    completed30d,
                    total30d,
                    avgOnboardingMinutes,
                    stuck24h,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                },
            });
        });

        admin.MapGet("/{renewalId:guid}", async (Guid renewalId, ApplicationDbContext db) =>
        {
            var renewal = await db.RenewalRequests
                .AsNoTracking()
                .Include(r => r.SearchResult)
                    .ThenInclude(sr => sr.SearchLog)
                .Include(r => r.Lead)
                .FirstOrDefaultAsync(r => r.Id == renewalId);

            if (renewal == null) return Results.NotFound();

            return Results.Ok(new
            {
                renewalRequestId = renewal.Id,
                leadId = renewal.LeadId,
                fullName = renewal.Lead?.FullName,
                email = renewal.Email,
                mobileNumber = renewal.MobileNumber,
                dateOfBirth = renewal.DateOfBirth,
                tfn = renewal.Tfn,
                abn = renewal.SearchResult?.SearchLog?.Abn,
                businessName = renewal.SearchResult?.BusinessName,
                initiatedAt = renewal.InitiatedAt,
                completedAt = renewal.CompletedAt,
                renewalStatus = renewal.Status.ToString(),
                atoJobId = renewal.AtoOnboardingJobId,
                atoStatus = renewal.AtoOnboardingStatus,
                atoCompletedAt = renewal.AtoOnboardingCompletedAt,
                atoResultJson = renewal.AtoOnboardingResultJson,
            });
        });

        // Re-enqueue a failed ATO onboarding by clearing the job marker so EnqueueAsync
        // will run again. Only valid for renewals whose ASIC step actually completed.
        admin.MapPost("/{renewalId:guid}/retry", async (
            Guid renewalId,
            ApplicationDbContext db,
            IAtoOnboardingService ato) =>
        {
            var renewal = await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == renewalId);
            if (renewal is null) return Results.NotFound();
            if (renewal.Status != RenewalStatus.Completed)
                return Results.BadRequest(new { error = "ATO onboarding can only be retried for completed renewals." });
            if (renewal.AtoOnboardingStatus != "Failed")
                return Results.BadRequest(new { error = "Only Failed onboardings are retryable." });

            renewal.AtoOnboardingJobId = null;
            renewal.AtoOnboardingStatus = null;
            renewal.AtoOnboardingResultJson = null;
            renewal.AtoOnboardingCompletedAt = null;
            await db.SaveChangesAsync();

            await ato.EnqueueAsync(renewalId);
            return Results.Ok(new { message = "ATO onboarding has been re-enqueued." });
        });

        // Bulk retry all Failed onboardings (24h window optional; fits the dashboard widget).
        admin.MapPost("/retry-all-failed", async (
            ApplicationDbContext db,
            IAtoOnboardingService ato) =>
        {
            var failed = await db.RenewalRequests
                .Where(r => r.AtoOnboardingStatus == "Failed" && r.Status == RenewalStatus.Completed)
                .ToListAsync();

            foreach (var r in failed)
            {
                r.AtoOnboardingJobId = null;
                r.AtoOnboardingStatus = null;
                r.AtoOnboardingResultJson = null;
                r.AtoOnboardingCompletedAt = null;
            }
            await db.SaveChangesAsync();

            foreach (var r in failed)
                await ato.EnqueueAsync(r.Id);

            return Results.Ok(new { retried = failed.Count });
        });
    }
}
