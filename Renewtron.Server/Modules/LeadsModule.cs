using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class LeadsModule : ICarterModule
{
    /// <summary>Query string of GET /api/admin/leads (all optional).</summary>
    public sealed class LeadListRequest
    {
        public string? Outcome { get; set; }
        public string? Reminder { get; set; }
        public string? Search { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? HasRenewal { get; set; }
        public int? Take { get; set; }
        public int? Page { get; set; }

        public LeadListQuery ToQuery() => new(Outcome, Reminder, Search, DateFrom, DateTo, HasRenewal, Take ?? 100, Page ?? 1);
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/leads", async (CreateLeadRequest request, LeadWizard wizard, HttpContext httpContext) =>
        {
            var (ip, ua) = Helpers.ClientInfo(httpContext);
            var result = await wizard.CaptureAsync(request, new RequestOrigin(ip, ua, httpContext.TraceIdentifier));
            return result.LeadId is { } leadId
                ? Results.Ok(new { leadId })
                : Results.BadRequest(new { error = result.Error });
        }).WithTags("Wizard").RequireRateLimiting("lead-capture"); // each call sends a lead email

        app.MapGet("/api/leads/{id:guid}", async (Guid id, ApplicationDbContext db, IOptionsSnapshot<TestModeSettings> testMode) =>
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
                // A test customer pays with Stripe's test keys, so the payment page needs them.
                testMode = lead.IsTest,
                testStripePublishableKey = lead.IsTest ? testMode.Value.StripePublishableKey : null,
                businessNames = lead.SearchLog?.Results.Where(r => r.IsAvailable).OrderBy(r => r.BusinessName).Select(r => new
                {
                    id = r.Id,
                    businessName = r.BusinessName,
                    accountNumber = r.AccountNumber,
                    registrationDate = r.RegistrationDate,
                }) ?? Enumerable.Empty<object>(),
            });
        }).WithTags("Wizard");

        app.MapPost("/api/leads/{id:guid}/check", async (Guid id, LeadWizard wizard) =>
        {
            var result = await wizard.CheckNamesAsync(id);
            if (result is null) return Results.NotFound();
            var names = result.BusinessNames.Select(n => new
            {
                id = n.Id, businessName = n.BusinessName, accountNumber = n.AccountNumber, registrationDate = n.RegistrationDate,
            });
            // The wire shapes the wizard has always received: a message only when there are no names.
            return result.Outcome == LeadOutcome.RenewalAvailable
                ? Results.Ok(new { outcome = result.Outcome.ToString(), verified = result.Verified, businessNames = names })
                : Results.Ok(new { outcome = result.Outcome.ToString(), message = result.Message, verified = result.Verified, businessNames = names });
        }).WithTags("Wizard").RequireRateLimiting("asic-search"); // may run a live ASIC lookup + outcome email per call

        var admin = app.MapGroup("/api/admin/leads").RequireAuthorization().WithTags("Admin.Leads");

        admin.MapGet("/", async ([AsParameters] LeadListRequest request, ApplicationDbContext db) =>
            Results.Ok(await LeadAdminQueries.ListAsync(db, request.ToQuery())));

        admin.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var l = await db.Leads.AsNoTracking()
                .Include(x => x.SearchLog)
                    .ThenInclude(s => s!.Results)
                .Include(x => x.RenewalRequests)
                    .ThenInclude(r => r.SearchResult)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (l is null) return Results.NotFound();
            var client = await ClientRefs.ByIdAsync(db, l.ClientId) ?? await ClientRefs.ByAbnAsync(db, l.Abn);
            return Results.Ok(new
            {
                id = l.Id,
                client,
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
                progress = l.Progress.ToString(),
                namesShownAt = l.NamesShownAt,
                reachedPaymentAt = l.ReachedPaymentAt,
                paymentFollowUpSentAt = l.PaymentFollowUpSentAt,
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
                        isAvailable = r.IsAvailable,
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
                    orderId = r.RenewalOrderId,
                }),
            });
        });
    }
}
