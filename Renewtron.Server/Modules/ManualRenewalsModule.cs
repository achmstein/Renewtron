using Asic.Integrations.Renewal;
using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

/// <summary>Staff renewing a name by hand: search ASIC for an ABN, then queue the renewal.</summary>
public sealed class ManualRenewalsModule : ICarterModule
{
    public record ManualRenewalRequest(
        string Abn,
        string BusinessName,
        string AccountNumber,
        string? RegistrationDate,
        int RenewalYears,
        string? Email);

    public record ManualSearchRequest(string Abn);

    public record ManualRenewalSubmitRequest(
        Guid SearchResultId,
        int RenewalYears,
        string Email,
        string? MobileNumber,
        DateOnly? DateOfBirth,
        decimal Amount);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/manual-search", async (
            ManualSearchRequest request,
            ApplicationDbContext db,
            IAsicRenewalClient asic,
            IBusinessNameFallbackService fallback,
            IOptionsSnapshot<AsicSettings> asicSettings,
            HttpContext httpContext) =>
        {
            if (!Helpers.IsValidAbn(request.Abn))
                return Results.BadRequest(new { error = "Please enter a valid 11-digit ABN" });

            var abn = Helpers.NormalizeAbn(request.Abn);
            var (ip, ua) = Helpers.ClientInfo(httpContext);

            BusinessNamesResult result;
            var source = asicSettings.Value.ForceFallback ? SearchSource.Local : SearchSource.Asic;
            try
            {
                result = asicSettings.Value.ForceFallback
                    ? await fallback.SearchByAbnAsync(abn)
                    : await asic.SearchByAbnAsync(abn);

                if (!result.Success && !asicSettings.Value.ForceFallback)
                {
                    // Names from our copy of the register — no ASIC account numbers.
                    var fb = await fallback.SearchByAbnAsync(abn);
                    if (fb.Success) { result = fb; source = SearchSource.Local; }
                }
            }
            catch (Exception ex)
            {
                db.SearchLogs.Add(new SearchLog
                {
                    Id = Guid.NewGuid(),
                    Abn = abn,
                    SearchedAt = DateTime.UtcNow,
                    IpAddress = ip,
                    UserAgent = ua,
                    SessionId = httpContext.TraceIdentifier,
                    Success = false,
                    InitiatedBy = SearchInitiator.Admin,
                    ErrorMessage = $"Exception: {ex.Message}",
                    ResultsCount = 0,
                });
                await db.SaveChangesAsync();
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }

            if (!result.Success || result.BusinessNames.Count == 0)
            {
                db.SearchLogs.Add(new SearchLog
                {
                    Id = Guid.NewGuid(),
                    Abn = abn,
                    SearchedAt = DateTime.UtcNow,
                    IpAddress = ip,
                    UserAgent = ua,
                    SessionId = httpContext.TraceIdentifier,
                    Success = result.Success,
                    InitiatedBy = SearchInitiator.Admin,
                    ErrorMessage = result.Success ? null : result.ErrorMessage,
                    ResultsCount = 0,
                });
                await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    success = false,
                    errorMessage = result.Success ? "No business names found for this ABN" : (result.ErrorMessage ?? "Failed to search ASIC renewal service"),
                    results = Array.Empty<object>(),
                });
            }

            var searchLog = new SearchLog
            {
                Id = Guid.NewGuid(),
                Abn = abn,
                SearchedAt = DateTime.UtcNow,
                Source = source,
                IpAddress = ip,
                UserAgent = ua,
                SessionId = httpContext.TraceIdentifier,
                Success = true,
                InitiatedBy = SearchInitiator.Admin,
                ResultsCount = result.BusinessNames.Count,
            };
            var saved = result.BusinessNames.Select(b => new SearchResult
            {
                Id = Guid.NewGuid(),
                SearchLogId = searchLog.Id,
                BusinessName = b.Name,
                AccountNumber = b.AccountNumber,
                RegistrationDate = b.RegistrationDate,
            }).ToList();
            searchLog.Results = saved;
            db.SearchLogs.Add(searchLog);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                results = saved.Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }),
            });
        }).RequireAuthorization().WithTags("Admin.Renewals");

        app.MapPost("/api/admin/manual-renewal/submit", async (
            ManualRenewalSubmitRequest request,
            ApplicationDbContext db,
            IBackgroundJobClient jobs) =>
        {
            if (request.SearchResultId == Guid.Empty)
                return Results.BadRequest(new { error = "Please select a business" });
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new { error = "Please enter customer email" });
            if (request.Amount <= 0)
                return Results.BadRequest(new { error = "Please enter amount paid by customer" });
            if (request.RenewalYears != 1 && request.RenewalYears != 3)
                return Results.BadRequest(new { error = "Renewal years must be 1 or 3" });

            var searchResult = await db.SearchResults.FirstOrDefaultAsync(r => r.Id == request.SearchResultId);
            if (searchResult is null) return Results.NotFound(new { error = "Search result not found" });

            var renewal = new RenewalRequest
            {
                Id = Guid.NewGuid(),
                SearchResultId = request.SearchResultId,
                InitiatedAt = DateTime.UtcNow,
                RenewalYears = request.RenewalYears,
                Email = request.Email,
                MobileNumber = request.MobileNumber,
                DateOfBirth = request.DateOfBirth,
                Source = RenewalSource.Renewtron,
                PaymentType = PaymentType.External,
                Amount = request.Amount,
                Status = RenewalStatus.Pending,
            };
            db.RenewalRequests.Add(renewal);
            await db.SaveChangesAsync();

            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewal.Id));
            RenewalOrderService.EnqueueRefresh(jobs);

            return Results.Ok(new
            {
                renewalId = renewal.Id,
                businessName = searchResult.BusinessName,
                message = $"Manual renewal queued successfully for {searchResult.BusinessName}. The renewal will be processed shortly.",
            });
        }).RequireAuthorization().WithTags("Admin.Renewals");

        // Queues the ASIC renewal instead of running it inline: the flow can take 10+ minutes
        // (OTP waits) and an aborted HTTP request used to strand the row in Processing.
        // Callers poll GET /api/admin/renewals/{id} for the outcome.
        app.MapPost("/api/admin/manual-renewal", async (
            ManualRenewalRequest request,
            ApplicationDbContext db,
            IBackgroundJobClient jobs,
            IOptionsSnapshot<PricingSettings> pricing) =>
        {
            if (!Helpers.IsValidAbn(request.Abn))
                return Results.BadRequest(new { error = "ABN must be 11 digits." });
            if (request.RenewalYears != 1 && request.RenewalYears != 3)
                return Results.BadRequest(new { error = "RenewalYears must be 1 or 3." });
            if (string.IsNullOrWhiteSpace(request.AccountNumber))
                return Results.BadRequest(new { error = "Account number is required." });
            if (string.IsNullOrWhiteSpace(request.BusinessName))
                return Results.BadRequest(new { error = "Business name is required." });

            var abn = Helpers.NormalizeAbn(request.Abn);

            var log = new SearchLog
            {
                Id = Guid.NewGuid(),
                Abn = abn,
                SearchedAt = DateTime.UtcNow,
                Success = true,
                ResultsCount = 1,
                InitiatedBy = SearchInitiator.Admin,
            };
            var searchResult = new SearchResult
            {
                Id = Guid.NewGuid(),
                SearchLogId = log.Id,
                BusinessName = request.BusinessName,
                AccountNumber = request.AccountNumber,
                RegistrationDate = request.RegistrationDate ?? string.Empty,
            };
            log.Results.Add(searchResult);
            db.SearchLogs.Add(log);

            var renewal = new RenewalRequest
            {
                Id = Guid.NewGuid(),
                SearchResultId = searchResult.Id,
                InitiatedAt = DateTime.UtcNow,
                RenewalYears = request.RenewalYears,
                Email = request.Email,
                Source = RenewalSource.Renewtron,
                PaymentType = PaymentType.External,
                Amount = pricing.Value.GetCustomerPrice(request.RenewalYears),
                Status = RenewalStatus.Pending,
            };
            db.RenewalRequests.Add(renewal);
            await db.SaveChangesAsync();

            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewal.Id));
            RenewalOrderService.EnqueueRefresh(jobs);

            return Results.Ok(new
            {
                renewalId = renewal.Id,
                status = renewal.Status.ToString(),
                message = "Renewal queued. Poll /api/admin/renewals/{id} for the outcome.",
            });
        }).RequireAuthorization().WithTags("Admin.Renewals");
    }
}
