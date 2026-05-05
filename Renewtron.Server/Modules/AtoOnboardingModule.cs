using Carter;
using Microsoft.EntityFrameworkCore;
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

            IQueryable<RenewalRequest> query = db.RenewalRequests
                .AsNoTracking()
                .Where(r => r.AtoOnboardingJobId != null);

            var totalCount = await query.CountAsync();
            var pendingCount = await query.CountAsync(r => r.AtoOnboardingStatus == "Pending" || r.AtoOnboardingStatus == "InProgress" || r.AtoOnboardingStatus == "AwaitingAuth");
            var completedCount = await query.CountAsync(r => r.AtoOnboardingStatus == "Completed");
            var failedCount = await query.CountAsync(r => r.AtoOnboardingStatus == "Failed");

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
                })
                .ToListAsync();

            return Results.Ok(new { totalCount, pendingCount, completedCount, failedCount, items });
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
    }
}
