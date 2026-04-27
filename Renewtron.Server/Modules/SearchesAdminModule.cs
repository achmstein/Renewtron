using Carter;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Modules;

public sealed class SearchesAdminModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/searches").RequireAuthorization().WithTags("Admin.Searches");

        group.MapGet("/", async (ApplicationDbContext db, int skip = 0, int take = 25) =>
        {
            take = Math.Clamp(take, 1, 200);
            var query = db.SearchLogs.AsNoTracking().OrderByDescending(s => s.SearchedAt);
            return Results.Ok(new
            {
                total = await query.CountAsync(),
                items = await query.Skip(skip).Take(take).Select(s => new
                {
                    id = s.Id,
                    abn = s.Abn,
                    searchedAt = s.SearchedAt,
                    success = s.Success,
                    resultsCount = s.ResultsCount,
                    initiatedBy = s.InitiatedBy.ToString(),
                }).ToListAsync(),
            });
        });

        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var s = await db.SearchLogs.AsNoTracking()
                .Include(x => x.Results)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            return Results.Ok(new
            {
                id = s.Id,
                abn = s.Abn,
                searchedAt = s.SearchedAt,
                success = s.Success,
                errorMessage = s.ErrorMessage,
                ipAddress = s.IpAddress,
                userAgent = s.UserAgent,
                results = s.Results.Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }),
            });
        });
    }
}
