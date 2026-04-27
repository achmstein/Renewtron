using Asic.Client.Abstractions;
using Asic.Client.Models;
using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class LeadsModule : ICarterModule
{
    public record CreateLeadRequest(
        string Abn,
        string FullName,
        string Email,
        string? MobileNumber,
        DateOnly DateOfBirth,
        Guid? SearchLogId);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/leads", async (
            CreateLeadRequest request,
            ILeadService leadService,
            ILeadEmailService leadEmail,
            HttpContext httpContext) =>
        {
            if (!Helpers.IsValidAbn(request.Abn))
                return Results.BadRequest(new { error = "ABN must be 11 digits." });
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new { error = "Email is required." });
            if (string.IsNullOrWhiteSpace(request.FullName))
                return Results.BadRequest(new { error = "Full name is required." });

            var (ip, ua) = Helpers.ClientInfo(httpContext);

            var lead = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Abn = Helpers.NormalizeAbn(request.Abn),
                FullName = request.FullName,
                Email = request.Email,
                MobileNumber = request.MobileNumber ?? string.Empty,
                DateOfBirth = request.DateOfBirth,
                IpAddress = ip,
                UserAgent = ua,
                SessionId = httpContext.TraceIdentifier,
            });

            if (request.SearchLogId is { } sid)
                await leadService.LinkSearchLogAsync(lead.Id, sid);

            try { await leadEmail.SendLeadCapturedEmailAsync(lead); } catch { }

            return Results.Ok(new { leadId = lead.Id });
        }).WithTags("Wizard");

        app.MapGet("/api/leads/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var lead = await db.Leads.AsNoTracking()
                .Include(l => l.SearchLog)
                    .ThenInclude(s => s!.Results)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lead is null) return Results.NotFound();

            return Results.Ok(new
            {
                id = lead.Id,
                abn = lead.Abn,
                fullName = lead.FullName,
                email = lead.Email,
                mobileNumber = lead.MobileNumber,
                dateOfBirth = lead.DateOfBirth,
                outcome = lead.Outcome.ToString(),
                outcomeMessage = lead.OutcomeMessage,
                businessNames = lead.SearchLog?.Results.Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }) ?? Enumerable.Empty<object>(),
            });
        }).WithTags("Wizard");

        app.MapPost("/api/leads/{id:guid}/check", async (
            Guid id,
            ApplicationDbContext db,
            ILeadService leadService,
            ILeadEmailService leadEmailService,
            IAsicRenewalClient asic,
            IBusinessNameFallbackService fallback,
            IOptionsSnapshot<AsicSettings> asicSettings,
            IMemoryCache cache) =>
        {
            var lead = await leadService.GetLeadAsync(id);
            if (lead is null) return Results.NotFound();

            var cleanAbn = Helpers.NormalizeAbn(lead.Abn);
            var cacheKey = $"abn_search_{cleanAbn}";

            BusinessNamesResult searchResult;
            if (!cache.TryGetValue(cacheKey, out BusinessNamesResult? cached) || cached is null)
            {
                if (asicSettings.Value.ForceFallback)
                {
                    searchResult = await fallback.SearchByAbnAsync(cleanAbn);
                }
                else
                {
                    searchResult = await asic.SearchByAbnAsync(cleanAbn);
                    if (!searchResult.Success)
                    {
                        var fb = await fallback.SearchByAbnAsync(cleanAbn);
                        if (fb.Success) searchResult = fb;
                    }
                }
                cache.Set(cacheKey, searchResult, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(15)));
            }
            else
            {
                searchResult = cached;
            }

            if (!searchResult.Success || searchResult.BusinessNames.Count == 0)
            {
                var failedLog = new SearchLog
                {
                    Id = Guid.NewGuid(),
                    Abn = cleanAbn,
                    SearchedAt = DateTime.UtcNow,
                    IpAddress = lead.IpAddress,
                    UserAgent = lead.UserAgent,
                    SessionId = lead.SessionId,
                    Success = searchResult.Success,
                    InitiatedBy = SearchInitiator.Customer,
                    ErrorMessage = searchResult.ErrorMessage ?? (searchResult.Success ? "No business names found" : "Search failed"),
                    ResultsCount = 0,
                };
                db.SearchLogs.Add(failedLog);
                await db.SaveChangesAsync();
                await leadService.LinkSearchLogAsync(lead.Id, failedLog.Id);

                var errorMsg = searchResult.ErrorMessage ?? "";
                LeadOutcome outcome;
                if (errorMsg.Contains("already in progress", StringComparison.OrdinalIgnoreCase))
                    outcome = LeadOutcome.RenewalInProgress;
                else if (errorMsg.Contains("not due for renewal", StringComparison.OrdinalIgnoreCase))
                    outcome = LeadOutcome.NotDueForRenewal;
                else
                    outcome = LeadOutcome.NoBusinessNames;

                await leadService.UpdateLeadOutcomeAsync(lead.Id, outcome, errorMsg);
                try
                {
                    var updated = await leadService.GetLeadAsync(lead.Id);
                    if (updated is not null)
                    {
                        switch (outcome)
                        {
                            case LeadOutcome.NotDueForRenewal: await leadEmailService.SendNotDueForRenewalEmailAsync(updated); break;
                            case LeadOutcome.RenewalInProgress: await leadEmailService.SendRenewalInProgressEmailAsync(updated); break;
                            case LeadOutcome.NoBusinessNames: await leadEmailService.SendNoBusinessNamesEmailAsync(updated); break;
                        }
                    }
                }
                catch { }

                return Results.Ok(new { outcome = outcome.ToString(), message = errorMsg, businessNames = Array.Empty<object>() });
            }

            // Successful search
            var searchLog = new SearchLog
            {
                Id = Guid.NewGuid(),
                Abn = cleanAbn,
                SearchedAt = DateTime.UtcNow,
                IpAddress = lead.IpAddress,
                UserAgent = lead.UserAgent,
                SessionId = lead.SessionId,
                Success = true,
                InitiatedBy = SearchInitiator.Customer,
                ResultsCount = searchResult.BusinessNames.Count,
            };
            var savedResults = searchResult.BusinessNames.Select(b => new SearchResult
            {
                Id = Guid.NewGuid(),
                SearchLogId = searchLog.Id,
                BusinessName = b.Name,
                AccountNumber = b.AccountNumber,
                RegistrationDate = b.RegistrationDate,
            }).ToList();
            searchLog.Results = savedResults;
            db.SearchLogs.Add(searchLog);
            await db.SaveChangesAsync();
            await leadService.LinkSearchLogAsync(lead.Id, searchLog.Id);
            await leadService.UpdateLeadOutcomeAsync(lead.Id, LeadOutcome.RenewalAvailable, null);

            return Results.Ok(new
            {
                outcome = LeadOutcome.RenewalAvailable.ToString(),
                businessNames = savedResults.Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }),
            });
        }).WithTags("Wizard");

        var admin = app.MapGroup("/api/admin/leads").RequireAuthorization().WithTags("Admin.Leads");

        admin.MapGet("/", async (
            ApplicationDbContext db,
            string? outcome = null,
            string? reminder = null,
            string? search = null,
            int take = 100) =>
        {
            take = Math.Clamp(take, 1, 500);

            var totalCount = await db.Leads.CountAsync();
            var convertedCount = await db.Leads.CountAsync(l => l.Outcome == LeadOutcome.RenewalCompleted);
            var notDueCount = await db.Leads.CountAsync(l => l.Outcome == LeadOutcome.NotDueForRenewal);
            var reminderCount = await db.Leads.CountAsync(l => l.ReminderOptIn);

            IQueryable<Lead> query = db.Leads.AsNoTracking();
            if (!string.IsNullOrEmpty(outcome) && Enum.TryParse<LeadOutcome>(outcome, out var oc))
                query = query.Where(l => l.Outcome == oc);
            if (!string.IsNullOrEmpty(reminder))
            {
                var on = reminder == "true";
                query = query.Where(l => l.ReminderOptIn == on);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(l =>
                    l.Abn.Contains(term) ||
                    l.FullName.ToLower().Contains(term) ||
                    l.Email.ToLower().Contains(term));
            }

            var items = await query
                .OrderByDescending(l => l.CreatedAt)
                .Take(take)
                .Select(l => new
                {
                    id = l.Id,
                    abn = l.Abn,
                    fullName = l.FullName,
                    email = l.Email,
                    mobileNumber = l.MobileNumber,
                    createdAt = l.CreatedAt,
                    outcome = l.Outcome.ToString(),
                    reminderOptIn = l.ReminderOptIn,
                    convertedToRenewal = l.ConvertedToRenewal,
                })
                .ToListAsync();

            return Results.Ok(new
            {
                totalCount,
                convertedCount,
                notDueCount,
                reminderCount,
                items,
            });
        });

        admin.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var l = await db.Leads.AsNoTracking()
                .Include(x => x.SearchLog)
                    .ThenInclude(s => s!.Results)
                .Include(x => x.RenewalRequests)
                    .ThenInclude(r => r.SearchResult)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (l is null) return Results.NotFound();
            return Results.Ok(new
            {
                id = l.Id,
                abn = l.Abn,
                fullName = l.FullName,
                email = l.Email,
                mobileNumber = l.MobileNumber,
                dateOfBirth = l.DateOfBirth,
                createdAt = l.CreatedAt,
                ipAddress = l.IpAddress,
                userAgent = l.UserAgent,
                sessionId = l.SessionId,
                outcome = l.Outcome.ToString(),
                outcomeMessage = l.OutcomeMessage,
                reminderOptIn = l.ReminderOptIn,
                convertedToRenewal = l.ConvertedToRenewal,
                convertedAt = l.ConvertedAt,
                searchLog = l.SearchLog == null ? null : new
                {
                    id = l.SearchLog.Id,
                    searchedAt = l.SearchLog.SearchedAt,
                    success = l.SearchLog.Success,
                    errorMessage = l.SearchLog.ErrorMessage,
                    resultsCount = l.SearchLog.ResultsCount,
                    results = l.SearchLog.Results.Select(r => new
                    {
                        id = r.Id,
                        businessName = r.BusinessName,
                        accountNumber = r.AccountNumber,
                        registrationDate = r.RegistrationDate,
                    }),
                },
                renewalRequests = l.RenewalRequests.Select(r => new
                {
                    id = r.Id,
                    status = r.Status.ToString(),
                    amount = r.Amount,
                    renewalYears = r.RenewalYears,
                    initiatedAt = r.InitiatedAt,
                    businessName = r.SearchResult != null ? r.SearchResult.BusinessName : null,
                }),
            });
        });
    }
}
