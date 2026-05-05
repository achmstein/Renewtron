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

public sealed class RenewalsModule : ICarterModule
{
    public record CreateRenewalRequest(
        Guid SearchResultId,
        Guid? LeadId,
        int RenewalYears,
        string? Email,
        string? MobileNumber,
        DateOnly? DateOfBirth,
        string PaymentMethodId);

    public record BatchRenewalRequest(
        Guid LeadId,
        Guid[] SearchResultIds,
        int RenewalYears,
        string PaymentMethodId,
        string? CardholderName);

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
        app.MapPost("/api/renewals", async (
            CreateRenewalRequest request,
            ApplicationDbContext db,
            IStripePaymentService stripe,
            IBackgroundJobClient jobs,
            ILeadService leadService,
            IOptionsSnapshot<PricingSettings> pricing) =>
        {
            var searchResult = await db.SearchResults
                .Include(r => r.SearchLog)
                .FirstOrDefaultAsync(r => r.Id == request.SearchResultId);

            if (searchResult is null)
                return Results.NotFound(new { error = "Search result not found." });

            if (request.RenewalYears != 1 && request.RenewalYears != 3)
                return Results.BadRequest(new { error = "RenewalYears must be 1 or 3." });

            var existing = await db.RenewalRequests
                .FirstOrDefaultAsync(r => r.SearchResultId == searchResult.Id);
            if (existing is not null)
                return Results.Conflict(new { error = "A renewal already exists for this business name.", renewalId = existing.Id });

            var amount = pricing.Value.GetCustomerPrice(request.RenewalYears);

            var renewal = new RenewalRequest
            {
                Id = Guid.NewGuid(),
                SearchResultId = searchResult.Id,
                LeadId = request.LeadId,
                InitiatedAt = DateTime.UtcNow,
                RenewalYears = request.RenewalYears,
                Email = request.Email,
                MobileNumber = request.MobileNumber,
                DateOfBirth = request.DateOfBirth,
                Source = RenewalSource.Renewtron,
                PaymentType = PaymentType.Stripe,
                Amount = amount,
                Status = RenewalStatus.Pending,
            };
            db.RenewalRequests.Add(renewal);
            await db.SaveChangesAsync();

            var (ok, paymentIntentId, stripeError) = await stripe.ConfirmPaymentAsync(
                amount,
                request.Email ?? string.Empty,
                $"Business name renewal: {searchResult.BusinessName} ({searchResult.SearchLog.Abn})",
                new Dictionary<string, string>
                {
                    ["renewalRequestId"] = renewal.Id.ToString(),
                    ["abn"] = searchResult.SearchLog.Abn,
                    ["businessName"] = searchResult.BusinessName,
                    ["renewalYears"] = renewal.RenewalYears.ToString(),
                },
                request.PaymentMethodId);

            if (!ok)
            {
                renewal.Status = RenewalStatus.Failed;
                renewal.ErrorMessage = stripeError;
                renewal.FailedAtStep = "StripePayment";
                await db.SaveChangesAsync();
                return Results.UnprocessableEntity(new { error = stripeError ?? "Payment failed.", renewalId = renewal.Id });
            }

            db.StripePayments.Add(new StripePayment
            {
                Id = Guid.NewGuid(),
                RenewalRequestId = renewal.Id,
                PaymentIntentId = paymentIntentId!,
                PaymentStatus = "succeeded",
                PaidAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewal.Id));

            if (request.LeadId is { } leadId)
            {
                try { await leadService.MarkConvertedAsync(leadId); } catch { }
            }

            return Results.Ok(new { renewalId = renewal.Id, status = renewal.Status.ToString(), amount });
        }).WithTags("Wizard");

        app.MapPost("/api/renewals/batch", async (
            BatchRenewalRequest request,
            ApplicationDbContext db,
            IStripePaymentService stripe,
            IBackgroundJobClient jobs,
            ILeadService leadService,
            IOptionsSnapshot<PricingSettings> pricing) =>
        {
            if (request.RenewalYears != 1 && request.RenewalYears != 3)
                return Results.BadRequest(new { error = "RenewalYears must be 1 or 3." });
            if (request.SearchResultIds.Length == 0)
                return Results.BadRequest(new { error = "At least one business name is required." });

            var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId);
            if (lead is null) return Results.NotFound(new { error = "Lead not found." });

            var searchResults = await db.SearchResults
                .Include(sr => sr.SearchLog)
                .Where(sr => request.SearchResultIds.Contains(sr.Id))
                .ToListAsync();

            if (searchResults.Count == 0)
                return Results.NotFound(new { error = "Search results not found." });

            var pricePer = pricing.Value.GetCustomerPrice(request.RenewalYears);
            var total = pricePer * searchResults.Count;
            var businessNames = string.Join(", ", searchResults.Select(s => s.BusinessName));

            var (ok, paymentIntentId, stripeError) = await stripe.ConfirmPaymentAsync(
                total,
                lead.Email,
                $"Business Name Renewal - {searchResults.Count} renewal(s)",
                new Dictionary<string, string>
                {
                    ["renewal_count"] = searchResults.Count.ToString(),
                    ["business_names"] = businessNames.Length > 500 ? businessNames[..500] : businessNames,
                    ["renewal_years"] = request.RenewalYears.ToString(),
                    ["lead_id"] = lead.Id.ToString(),
                },
                request.PaymentMethodId);

            if (!ok)
                return Results.UnprocessableEntity(new { error = stripeError ?? "Payment failed." });

            var renewalIds = new List<Guid>();
            foreach (var sr in searchResults)
            {
                var existing = await db.RenewalRequests
                    .Include(r => r.StripePayment)
                    .FirstOrDefaultAsync(r => r.SearchResultId == sr.Id);

                RenewalRequest renewal;
                if (existing is not null)
                {
                    renewal = existing;
                    renewal.LeadId = lead.Id;
                    renewal.RenewalYears = request.RenewalYears;
                    renewal.Email = lead.Email;
                    renewal.MobileNumber = lead.MobileNumber;
                    renewal.DateOfBirth = lead.DateOfBirth;
                    renewal.Tfn = lead.Tfn;
                    renewal.PaymentType = PaymentType.Stripe;
                    renewal.Amount = pricePer;
                    renewal.Status = RenewalStatus.Pending;
                    renewal.CompletedAt = null;
                    renewal.TransactionReference = null;
                    renewal.HostedTokenizationId = null;
                    renewal.ErrorMessage = null;
                    renewal.FailedAtStep = null;
                    if (renewal.StripePayment is not null)
                        db.StripePayments.Remove(renewal.StripePayment);
                }
                else
                {
                    renewal = new RenewalRequest
                    {
                        Id = Guid.NewGuid(),
                        SearchResultId = sr.Id,
                        LeadId = lead.Id,
                        InitiatedAt = DateTime.UtcNow,
                        RenewalYears = request.RenewalYears,
                        Email = lead.Email,
                        MobileNumber = lead.MobileNumber,
                        DateOfBirth = lead.DateOfBirth,
                        Tfn = lead.Tfn,
                        Source = RenewalSource.Renewtron,
                        PaymentType = PaymentType.Stripe,
                        Amount = pricePer,
                        Status = RenewalStatus.Pending,
                    };
                    db.RenewalRequests.Add(renewal);
                }
                await db.SaveChangesAsync();

                db.StripePayments.Add(new StripePayment
                {
                    Id = Guid.NewGuid(),
                    RenewalRequestId = renewal.Id,
                    PaymentIntentId = paymentIntentId!,
                    PaymentStatus = "succeeded",
                    PaidAt = DateTime.UtcNow,
                    CardholderName = request.CardholderName,
                });
                await db.SaveChangesAsync();

                renewalIds.Add(renewal.Id);
            }

            try { await leadService.MarkConvertedAsync(lead.Id); } catch { }

            foreach (var rid in renewalIds)
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(rid));

            return Results.Ok(new { renewalIds, total });
        }).WithTags("Wizard");

        app.MapGet("/api/renewals/batch", async (string ids, ApplicationDbContext db) =>
        {
            var idList = (ids ?? "").Split(',').Where(s => Guid.TryParse(s, out _)).Select(Guid.Parse).ToArray();
            if (idList.Length == 0) return Results.BadRequest(new { error = "ids required" });

            var renewals = await db.RenewalRequests.AsNoTracking()
                .Include(r => r.SearchResult)
                .Where(r => idList.Contains(r.Id))
                .ToListAsync();

            return Results.Ok(renewals.Select(r => new
            {
                id = r.Id,
                status = r.Status.ToString(),
                amount = r.Amount,
                renewalYears = r.RenewalYears,
                businessName = r.SearchResult?.BusinessName,
                accountNumber = r.SearchResult?.AccountNumber,
                transactionReference = r.TransactionReference,
                errorMessage = r.ErrorMessage,
            }));
        }).WithTags("Wizard");

        app.MapGet("/api/renewals/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var renewal = await db.RenewalRequests.AsNoTracking()
                .Include(r => r.SearchResult).ThenInclude(sr => sr.SearchLog)
                .Include(r => r.StripePayment)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (renewal is null) return Results.NotFound();

            return Results.Ok(new
            {
                id = renewal.Id,
                status = renewal.Status.ToString(),
                businessName = renewal.SearchResult.BusinessName,
                abn = renewal.SearchResult.SearchLog.Abn,
                renewalYears = renewal.RenewalYears,
                amount = renewal.Amount,
                completedAt = renewal.CompletedAt,
                transactionReference = renewal.TransactionReference,
                errorMessage = renewal.ErrorMessage,
            });
        }).WithTags("Wizard");

        var admin = app.MapGroup("/api/admin/renewals").RequireAuthorization().WithTags("Admin.Renewals");

        admin.MapGet("/", async (
            ApplicationDbContext db,
            string? abn = null,
            string? status = null,
            string? initiatedBy = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            int page = 1,
            int pageSize = 10) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            IQueryable<RenewalRequest> query = db.RenewalRequests.AsNoTracking()
                .Include(r => r.SearchResult).ThenInclude(s => s.SearchLog)
                .Include(r => r.StripePayment);

            if (!string.IsNullOrWhiteSpace(abn))
                query = query.Where(r => r.SearchResult.SearchLog.Abn.Contains(abn));
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RenewalStatus>(status, true, out var st))
                query = query.Where(r => r.Status == st);
            if (!string.IsNullOrWhiteSpace(initiatedBy) && Enum.TryParse<SearchInitiator>(initiatedBy, true, out var ini))
                query = query.Where(r => r.SearchResult.SearchLog.InitiatedBy == ini);
            if (dateFrom.HasValue)
                query = query.Where(r => r.InitiatedAt >= dateFrom.Value);
            if (dateTo.HasValue)
            {
                var dt = dateTo.Value.AddDays(1);
                query = query.Where(r => r.InitiatedAt < dt);
            }

            var totalCount = await query.CountAsync();

            var rows = await query
                .OrderByDescending(r => r.InitiatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    id = r.Id,
                    abn = r.SearchResult.SearchLog.Abn,
                    businessName = r.SearchResult.BusinessName,
                    renewalYears = r.RenewalYears,
                    amount = r.Amount,
                    status = r.Status.ToString(),
                    source = r.Source.ToString(),
                    paymentType = r.PaymentType.ToString(),
                    initiatedAt = r.InitiatedAt,
                    completedAt = r.CompletedAt,
                    email = r.Email,
                    errorMessage = r.ErrorMessage,
                    transactionReference = r.TransactionReference,
                    initiatedByLabel = r.SearchResult.SearchLog.InitiatedBy.ToString(),
                    stripePaymentSucceeded = r.StripePayment != null && r.StripePayment.PaymentStatus == "succeeded",
                })
                .ToListAsync();

            return Results.Ok(new { totalCount, page, pageSize, items = rows });
        });

        admin.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
        {
            var r = await db.RenewalRequests.AsNoTracking()
                .Include(x => x.SearchResult).ThenInclude(s => s.SearchLog)
                .Include(x => x.StripePayment)
                .Include(x => x.Lead)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            return Results.Ok(new
            {
                id = r.Id,
                status = r.Status.ToString(),
                source = r.Source.ToString(),
                paymentType = r.PaymentType.ToString(),
                businessName = r.SearchResult.BusinessName,
                accountNumber = r.SearchResult.AccountNumber,
                abn = r.SearchResult.SearchLog.Abn,
                ipAddress = r.SearchResult.SearchLog.IpAddress,
                initiatedByLabel = r.SearchResult.SearchLog.InitiatedBy.ToString(),
                renewalYears = r.RenewalYears,
                amount = r.Amount,
                email = r.Email,
                mobileNumber = r.MobileNumber,
                dateOfBirth = r.DateOfBirth,
                initiatedAt = r.InitiatedAt,
                completedAt = r.CompletedAt,
                transactionReference = r.TransactionReference,
                hostedTokenizationId = r.HostedTokenizationId,
                errorMessage = r.ErrorMessage,
                failedAtStep = r.FailedAtStep,
                stripePayment = r.StripePayment == null ? null : new
                {
                    paymentIntentId = r.StripePayment.PaymentIntentId,
                    paymentStatus = r.StripePayment.PaymentStatus,
                    paidAt = r.StripePayment.PaidAt,
                    cardholderName = r.StripePayment.CardholderName,
                    cardLast4 = r.StripePayment.CardLast4,
                    cardBrand = r.StripePayment.CardBrand,
                    cardExpMonth = r.StripePayment.CardExpMonth,
                    cardExpYear = r.StripePayment.CardExpYear,
                },
                lead = r.Lead == null ? null : new
                {
                    id = r.Lead.Id,
                    fullName = r.Lead.FullName,
                    email = r.Lead.Email,
                },
            });
        });

        admin.MapPost("/{id:guid}/retry", async (Guid id, ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            var renewal = await db.RenewalRequests
                .Include(r => r.StripePayment)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (renewal is null) return Results.NotFound(new { error = "Renewal request not found" });
            if (renewal.Status == RenewalStatus.Completed)
                return Results.BadRequest(new { error = "This renewal has already been completed successfully" });
            if (renewal.PaymentType == PaymentType.Stripe &&
                (renewal.StripePayment is null || renewal.StripePayment.PaymentStatus != "succeeded"))
                return Results.BadRequest(new { error = "Cannot retry: Payment was not successful. Customer needs to retry payment." });

            renewal.Status = RenewalStatus.Pending;
            renewal.ErrorMessage = null;
            renewal.FailedAtStep = null;
            await db.SaveChangesAsync();

            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(id));
            return Results.Ok(new { message = "Renewal has been queued for retry and will be processed shortly." });
        });

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
            try
            {
                result = asicSettings.Value.ForceFallback
                    ? await fallback.SearchByAbnAsync(abn)
                    : await asic.SearchByAbnAsync(abn);

                if (!result.Success && !asicSettings.Value.ForceFallback)
                {
                    var fb = await fallback.SearchByAbnAsync(abn);
                    if (fb.Success) result = fb;
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

            return Results.Ok(new
            {
                renewalId = renewal.Id,
                businessName = searchResult.BusinessName,
                message = $"Manual renewal queued successfully for {searchResult.BusinessName}. The renewal will be processed shortly.",
            });
        }).RequireAuthorization().WithTags("Admin.Renewals");

        app.MapPost("/api/admin/manual-renewal", async (
            ManualRenewalRequest request,
            ApplicationDbContext db,
            IAsicRenewalClient asic,
            IOptionsSnapshot<AsicSettings> asicSettings,
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
                Status = RenewalStatus.Processing,
            };
            db.RenewalRequests.Add(renewal);
            await db.SaveChangesAsync();

            var settings = asicSettings.Value;
            var card = new CreditCardDetails
            {
                CardNumber = settings.CardNumber,
                CardholderName = settings.CardholderName,
                ExpiryMonth = settings.ExpiryMonth,
                ExpiryYear = settings.ExpiryYear,
                Cvc = settings.Cvc,
            };

            var result = await asic.RenewBusinessNameAsync(
                abn,
                searchResult.AccountNumber,
                renewal.RenewalYears,
                settings.Email ?? string.Empty,
                card);

            renewal.Status = result.IsSuccess ? RenewalStatus.Completed : RenewalStatus.Failed;
            renewal.CompletedAt = result.IsSuccess ? DateTime.UtcNow : null;
            renewal.TransactionReference = result.TransactionReference;
            renewal.HostedTokenizationId = result.HostedTokenizationId;
            renewal.ErrorMessage = result.IsSuccess ? null : result.Message;
            renewal.FailedAtStep = result.IsSuccess ? null : result.FailedAtStep;
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                renewalId = renewal.Id,
                status = renewal.Status.ToString(),
                transactionReference = result.TransactionReference,
                errorMessage = result.Message,
            });
        }).RequireAuthorization().WithTags("Admin.Renewals");
    }
}
