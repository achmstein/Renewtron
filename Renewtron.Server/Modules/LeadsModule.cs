using Asic.Client.Abstractions;
using Asic.Client.Models;
using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
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
            IBackgroundJobClient jobs,
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
            jobs.Enqueue<IOntraportContactPushService>(s => s.PushLeadAsync(lead.Id));

            return Results.Ok(new { leadId = lead.Id });
        }).WithTags("Wizard").RequireRateLimiting("lead-capture"); // each call sends a lead email

        app.MapGet("/api/leads/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var lead = await db.Leads.AsNoTracking()
                .Include(l => l.SearchLog)
                    .ThenInclude(s => s!.Results)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lead is null) return Results.NotFound();

            // Anyone holding the lead id can call this, and the id sits in wizard URLs that
            // analytics tags see — so contact details come back masked, for display only.
            // Date of birth is not returned at all.
            return Results.Ok(new
            {
                id = lead.Id,
                abn = lead.Abn,
                fullName = PiiMask.Name(lead.FullName),
                email = PiiMask.Email(lead.Email),
                mobileNumber = PiiMask.Mobile(lead.MobileNumber),
                outcome = lead.Outcome.ToString(),
                outcomeMessage = lead.OutcomeMessage,
                // False while names from our copy of the register wait on ASIC's confirmation;
                // the wizard polls this and payment is refused until it flips.
                verified = lead.SearchLog?.IsVerified ?? true,
                businessNames = lead.SearchLog?.Results.Where(r => r.IsAvailable).OrderBy(r => r.BusinessName).Select(r => new
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
            IAsicSearchCoordinator asic,
            IBusinessNameFallbackService localNames,
            IOptionsSnapshot<AsicSettings> asicSettings,
            IServiceScopeFactory scopes,
            IBackgroundJobClient jobs,
            ILogger<LeadsModule> logger) =>
        {
            var lead = await leadService.GetLeadAsync(id);
            if (lead is null) return Results.NotFound();

            var cleanAbn = Helpers.NormalizeAbn(lead.Abn);

            // Fastest path that is still ASIC's answer: the search the ABN step started is done.
            // Otherwise list the names from our copy of the register straight away and confirm
            // them with ASIC in the background; payment waits for that. Only an ABN our copy
            // doesn't know (e.g. registered since the last import) waits on ASIC here.
            SearchSource source;
            BusinessNamesResult searchResult;
            if (!asicSettings.Value.ForceFallback && asic.TryGetRecent(cleanAbn, out var recent))
            {
                source = SearchSource.Asic;
                searchResult = recent;
            }
            else
            {
                if (!asicSettings.Value.ForceFallback) asic.Prefetch(cleanAbn);
                var local = await localNames.SearchByAbnAsync(cleanAbn);
                if (local.Success && local.BusinessNames.Count > 0)
                {
                    source = SearchSource.Local;
                    searchResult = local;
                }
                else if (asicSettings.Value.ForceFallback)
                {
                    source = SearchSource.Local;
                    searchResult = local;
                }
                else
                {
                    source = SearchSource.Asic;
                    searchResult = await asic.SearchAsync(cleanAbn);
                }
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
                    Source = source,
                    ErrorMessage = searchResult.ErrorMessage ?? (searchResult.Success ? "No business names found" : "Search failed"),
                    ResultsCount = 0,
                };
                db.SearchLogs.Add(failedLog);
                await db.SaveChangesAsync();
                await leadService.LinkSearchLogAsync(lead.Id, failedLog.Id);

                var errorMsg = searchResult.ErrorMessage ?? "";
                var outcome = LeadOutcomes.FromAsicError(errorMsg);
                await leadService.UpdateLeadOutcomeAsync(lead.Id, outcome, errorMsg);
                var updated = await leadService.GetLeadAsync(lead.Id);
                if (updated is not null)
                    await LeadOutcomes.SendEmailAsync(leadEmailService, updated, outcome, logger);

                return Results.Ok(new { outcome = outcome.ToString(), message = errorMsg, verified = true, businessNames = Array.Empty<object>() });
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
                Source = source,
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

            if (source == SearchSource.Asic)
                // The business names are ASIC's — send them to the lead's Ontraport contact.
                jobs.Enqueue<IOntraportContactPushService>(s => s.PushLeadAsync(lead.Id));
            else if (!asicSettings.Value.ForceFallback)
                // Pushes to Ontraport once ASIC has confirmed the names.
                SearchVerificationService.Start(scopes, jobs, searchLog.Id);

            return Results.Ok(new
            {
                outcome = LeadOutcome.RenewalAvailable.ToString(),
                verified = searchLog.IsVerified,
                businessNames = savedResults.Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }),
            });
        }).WithTags("Wizard").RequireRateLimiting("asic-search"); // may run a live ASIC lookup + outcome email per call

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

            // Facet params accept comma-separated multi-values ("RenewalAvailable,Pending").
            var outcomeValues = Helpers.ParseEnumList<LeadOutcome>(outcome);

            // Reminder round-trips as OptedIn/NotOptedIn (facet values); legacy true/false
            // still accepted. Both selected (or neither) means no filter.
            var reminderTokens = (reminder ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var wantOptedIn = reminderTokens.Any(t =>
                t.Equals("OptedIn", StringComparison.OrdinalIgnoreCase) || t.Equals("true", StringComparison.OrdinalIgnoreCase));
            var wantNotOptedIn = reminderTokens.Any(t =>
                t.Equals("NotOptedIn", StringComparison.OrdinalIgnoreCase) || t.Equals("false", StringComparison.OrdinalIgnoreCase));
            bool? reminderOn = wantOptedIn == wantNotOptedIn ? null : wantOptedIn;

            // One filter pipeline, with each facet dimension excludable so its own option
            // counts can be computed under the OTHER selections (classic faceted search).
            IQueryable<Lead> Filtered(bool applyOutcome = true, bool applyReminder = true)
            {
                IQueryable<Lead> q = db.Leads.AsNoTracking();
                if (applyOutcome && outcomeValues.Length > 0)
                    q = q.Where(l => outcomeValues.Contains(l.Outcome));
                if (applyReminder && reminderOn is { } on)
                    q = q.Where(l => l.ReminderOptIn == on);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.ToLower();
                    q = q.Where(l =>
                        l.Abn.Contains(term) ||
                        l.FullName.ToLower().Contains(term) ||
                        l.Email.ToLower().Contains(term));
                }
                if (dateFrom.HasValue)
                    q = q.Where(l => l.CreatedAt >= dateFrom.Value.ToUniversalTime());
                if (dateTo.HasValue)
                {
                    var until = dateTo.Value.Date.AddDays(1).ToUniversalTime();
                    q = q.Where(l => l.CreatedAt < until);
                }
                if (hasRenewal == "true")
                    q = q.Where(l => l.RenewalRequests.Any());
                else if (hasRenewal == "false")
                    q = q.Where(l => !l.RenewalRequests.Any());
                return q;
            }

            var query = Filtered();

            var outcomeFacet = await Filtered(applyOutcome: false)
                .GroupBy(l => l.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var reminderFacet = await Filtered(applyReminder: false)
                .GroupBy(l => l.ReminderOptIn).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

            var facets = new
            {
                outcome = outcomeFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
                reminder = reminderFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key ? "OptedIn" : "NotOptedIn", count = f.Count }),
            };

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
                facets,
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
