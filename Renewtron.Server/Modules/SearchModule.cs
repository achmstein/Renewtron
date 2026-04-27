using Asic.Client.Abstractions;
using Asic.Client.Models;
using Carter;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class SearchModule : ICarterModule
{
    public record SearchRequest(string Abn);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", async (
            SearchRequest request,
            IAsicRenewalClient asic,
            IBusinessNameFallbackService fallback,
            IOptionsSnapshot<AsicSettings> asicSettings,
            ApplicationDbContext db,
            HttpContext httpContext) =>
        {
            if (!Helpers.IsValidAbn(request.Abn))
                return Results.BadRequest(new { error = "ABN must be 11 digits." });

            var abn = Helpers.NormalizeAbn(request.Abn);
            var (ip, ua) = Helpers.ClientInfo(httpContext);

            var log = new SearchLog
            {
                Id = Guid.NewGuid(),
                Abn = abn,
                SearchedAt = DateTime.UtcNow,
                IpAddress = ip,
                UserAgent = ua,
                SessionId = httpContext.TraceIdentifier,
                InitiatedBy = SearchInitiator.Customer,
            };

            BusinessNamesResult result;
            try
            {
                result = asicSettings.Value.ForceFallback
                    ? await fallback.SearchByAbnAsync(abn)
                    : await asic.SearchByAbnAsync(abn);
            }
            catch (Exception ex)
            {
                log.Success = false;
                log.ErrorMessage = ex.Message;
                db.SearchLogs.Add(log);
                await db.SaveChangesAsync();
                return Results.Problem(detail: ex.Message, statusCode: 502);
            }

            log.Success = result.Success;
            log.ErrorMessage = result.ErrorMessage;
            log.ResultsCount = result.BusinessNames.Count;

            var savedResults = result.BusinessNames.Select(b => new SearchResult
            {
                Id = Guid.NewGuid(),
                SearchLogId = log.Id,
                BusinessName = b.Name,
                AccountNumber = b.AccountNumber,
                RegistrationDate = b.RegistrationDate,
            }).ToList();

            log.Results = savedResults;
            db.SearchLogs.Add(log);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                searchLogId = log.Id,
                success = result.Success,
                errorMessage = result.ErrorMessage,
                results = savedResults.Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }),
            });
        }).WithTags("Wizard");
    }
}
