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
    private static string? NormalizeTfn(string? tfn)
    {
        if (string.IsNullOrWhiteSpace(tfn)) return null;
        var digits = new string(tfn.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    public record CreateLeadRequest(
        string Abn,
        string FullName,
        string Email,
        string? MobileNumber,
        DateOnly DateOfBirth,
        string? Tfn,
        Guid? SearchLogId,
        string? Source,
        string? OntraportContactId,
        string? VisitorId);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

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
                Tfn = NormalizeTfn(request.Tfn),
                IpAddress = ip,
                UserAgent = ua,
                SessionId = httpContext.TraceIdentifier,
                Source = Trim(request.Source, 64),
                OntraportContactId = Trim(request.OntraportContactId, 20),
                VisitorId = Trim(request.VisitorId, 64),
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
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? hasRenewal = null,
            int take = 100) =>
        {
            take = Math.Clamp(take, 1, 500);

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
            if (dateFrom.HasValue)
                query = query.Where(l => l.CreatedAt >= dateFrom.Value.ToUniversalTime());
            if (dateTo.HasValue)
            {
                var until = dateTo.Value.Date.AddDays(1).ToUniversalTime();
                query = query.Where(l => l.CreatedAt < until);
            }
            if (hasRenewal == "true")
                query = query.Where(l => l.RenewalRequests.Any());
            else if (hasRenewal == "false")
                query = query.Where(l => !l.RenewalRequests.Any());

            var totalCount = await query.CountAsync();

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
                    source = l.Source,
                    outcome = l.Outcome.ToString(),
                    reminderOptIn = l.ReminderOptIn,
                    convertedToRenewal = l.ConvertedToRenewal,
                    convertedAt = l.ConvertedAt,
                    searchLog = l.SearchLog == null ? null : new
                    {
                        id = l.SearchLog.Id,
                        searchedAt = l.SearchLog.SearchedAt,
                        success = l.SearchLog.Success,
                        resultsCount = l.SearchLog.ResultsCount,
                    },
                    firstBusinessName = l.SearchLog == null
                        ? null
                        : l.SearchLog.Results.OrderBy(r => r.Id).Select(r => r.BusinessName).FirstOrDefault(),
                    renewal = l.RenewalRequests
                        .OrderByDescending(r => r.InitiatedAt)
                        .Select(r => new { id = r.Id, status = r.Status.ToString(), amount = r.Amount })
                        .FirstOrDefault(),
                })
                .ToListAsync();

            // ─────────── Stats block (computed against the same filter) ───────────
            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var fourteenDaysAgo = now.Date.AddDays(-13).ToUniversalTime();
            var todayStart = now.Date.ToUniversalTime();
            var yesterdayStart = todayStart.AddDays(-1);

            var totalAllTime = await db.Leads.AsNoTracking().CountAsync();

            var thirty = await query
                .Where(l => l.CreatedAt >= thirtyDaysAgo)
                .GroupBy(l => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Converted = g.Count(l => l.Outcome == LeadOutcome.RenewalCompleted),
                })
                .FirstOrDefaultAsync();
            var total30d = thirty?.Total ?? 0;
            var converted30d = thirty?.Converted ?? 0;
            var conversionRate30d = total30d > 0 ? Math.Round((decimal)converted30d * 100m / total30d, 1) : 0m;

            // Avg time-to-renewal (Lead.CreatedAt → first Completed RenewalRequest.CompletedAt)
            decimal? avgHoursToConvert = null;
            var convertedSamples = await db.Leads.AsNoTracking()
                .Where(l => l.Outcome == LeadOutcome.RenewalCompleted
                    && l.CreatedAt >= thirtyDaysAgo
                    && l.RenewalRequests.Any(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null))
                .Select(l => new
                {
                    l.CreatedAt,
                    CompletedAt = l.RenewalRequests
                        .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null)
                        .OrderBy(r => r.CompletedAt)
                        .Select(r => r.CompletedAt!.Value)
                        .FirstOrDefault(),
                })
                .Take(500)
                .ToListAsync();
            if (convertedSamples.Count > 0)
            {
                var totalHours = convertedSamples.Sum(s => (s.CompletedAt - s.CreatedAt).TotalHours);
                avgHoursToConvert = (decimal)Math.Round(totalHours / convertedSamples.Count, 1);
            }

            var todayCount = await query.CountAsync(l => l.CreatedAt >= todayStart);
            var yesterdayCount = await query.CountAsync(l => l.CreatedAt >= yesterdayStart && l.CreatedAt < todayStart);

            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            var dailyRaw = await query
                .Where(l => l.CreatedAt >= fourteenDaysAgo)
                .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month, l.CreatedAt.Day })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
                .ToListAsync();
            var dailyMap = dailyRaw.ToDictionary(d => new DateOnly(d.Year, d.Month, d.Day), d => d.Count);
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(now.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();

            var outcomeBreakdownRaw = await query
                .GroupBy(l => l.Outcome)
                .Select(g => new { Outcome = g.Key, Count = g.Count() })
                .ToListAsync();
            var outcomeBreakdown = outcomeBreakdownRaw.ToDictionary(b => b.Outcome.ToString(), b => b.Count);

            var winBackEligible = await query.CountAsync(l =>
                l.Outcome == LeadOutcome.RenewalAvailable && !l.ConvertedToRenewal && l.Email != null && l.Email != "");

            // Average basket — used to estimate $ recoverable from win-back eligible leads
            var basketStats = await db.RenewalRequests.AsNoTracking()
                .Where(r => r.Status == RenewalStatus.Completed && r.Amount > 0)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Amount) })
                .FirstOrDefaultAsync();
            var avgBasket = basketStats != null && basketStats.Count > 0 ? basketStats.Sum / basketStats.Count : 79m;

            return Results.Ok(new
            {
                totalCount,
                items,
                stats = new
                {
                    totalAllTime,
                    total30d,
                    converted30d,
                    conversionRate30d,
                    avgHoursToConvert,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                    outcomeBreakdown,
                    winBackEligible,
                    winBackRecoverableValue = winBackEligible * avgBasket,
                    avgBasket,
                },
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
                source = l.Source,
                ontraportContactId = l.OntraportContactId,
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
