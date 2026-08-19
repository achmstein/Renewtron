using System.Text;
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

    public record BatchRenewalCompleteRequest(
        Guid LeadId,
        Guid[] SearchResultIds,
        int RenewalYears,
        string PaymentIntentId,
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

            var confirm = await stripe.ConfirmPaymentAsync(
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

            if (!confirm.Success)
            {
                // This legacy endpoint has no browser round-trip, so a 3DS request is a dead end here.
                var stripeError = confirm.RequiresAction
                    ? "This card requires 3D Secure authentication, which this checkout does not support. Please pay via the website."
                    : confirm.ErrorMessage;
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
                PaymentIntentId = confirm.PaymentIntentId!,
                PaymentStatus = "succeeded",
                PaidAt = DateTime.UtcNow,
                CardBrand = confirm.Card?.Brand,
                CardLast4 = confirm.Card?.Last4,
                CardExpMonth = confirm.Card?.ExpMonth,
                CardExpYear = confirm.Card?.ExpYear,
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

            // A double-submit (double-click, client retry) must never charge twice: refuse
            // names that already have a succeeded payment, and pin the Stripe call with an
            // idempotency key derived from the batch's identity.
            var resultIds = searchResults.Select(sr => sr.Id).ToList();
            var alreadyPaid = await db.StripePayments
                .Where(p => p.PaymentStatus == "succeeded")
                .Where(p => db.RenewalRequests.Any(r => r.Id == p.RenewalRequestId && resultIds.Contains(r.SearchResultId)))
                .AnyAsync();
            if (alreadyPaid)
                return Results.Conflict(new { error = "One or more of these business names has already been paid for. Refresh the page to see the current status." });

            var pricePer = pricing.Value.GetCustomerPrice(request.RenewalYears);
            var total = pricePer * searchResults.Count;
            var businessNames = string.Join(", ", searchResults.Select(s => s.BusinessName));

            var idempotencySource = $"batch:{lead.Id}:{request.RenewalYears}:{string.Join(",", resultIds.OrderBy(id => id))}";
            var idempotencyKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(idempotencySource)));
            var idsHash = IdsHash(resultIds);

            var confirm = await stripe.ConfirmPaymentAsync(
                total,
                lead.Email,
                $"Business Name Renewal - {searchResults.Count} renewal(s)",
                new Dictionary<string, string>
                {
                    ["renewal_count"] = searchResults.Count.ToString(),
                    ["business_names"] = businessNames.Length > 500 ? businessNames[..500] : businessNames,
                    ["renewal_years"] = request.RenewalYears.ToString(),
                    ["lead_id"] = lead.Id.ToString(),
                    // Pins the intent to the exact names being paid for; /batch/complete
                    // verifies it so a paid intent can't be redirected to different names.
                    ["ids_hash"] = idsHash,
                },
                request.PaymentMethodId,
                idempotencyKey);

            if (confirm.RequiresAction)
            {
                // Card wants 3D Secure. Nothing is recorded yet — the browser runs
                // stripe.handleNextAction(clientSecret) and then posts /batch/complete.
                return Results.Ok(new
                {
                    renewalIds = Array.Empty<Guid>(),
                    total,
                    requiresAction = true,
                    clientSecret = confirm.ClientSecret,
                });
            }

            if (!confirm.Success)
                return Results.UnprocessableEntity(new { error = confirm.ErrorMessage ?? "Payment failed." });

            var renewalIds = await UpsertPaidRenewalsAsync(
                db, lead, searchResults, request.RenewalYears, pricePer,
                confirm.PaymentIntentId!, request.CardholderName, confirm.Card);

            try { await leadService.MarkConvertedAsync(lead.Id); } catch { }

            foreach (var rid in renewalIds)
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(rid));

            return Results.Ok(new { renewalIds, total, requiresAction = false, clientSecret = (string?)null });
        }).WithTags("Wizard");

        // Second leg of the 3DS flow: the browser has finished the challenge, we verify the
        // PaymentIntent with Stripe ourselves (never trust the client's word) and only then
        // record the renewals — the same writes the one-shot path above does.
        app.MapPost("/api/renewals/batch/complete", async (
            BatchRenewalCompleteRequest request,
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
            if (string.IsNullOrWhiteSpace(request.PaymentIntentId))
                return Results.BadRequest(new { error = "PaymentIntentId is required." });

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

            var intent = await stripe.GetPaymentIntentAsync(request.PaymentIntentId);
            if (intent is null)
                return Results.UnprocessableEntity(new { error = "Payment could not be verified. Please contact support." });
            if (intent.Status != "succeeded")
                return Results.UnprocessableEntity(new { error = $"Payment was not completed (status: {intent.Status}). You have not been charged — please try again." });
            if (!intent.Metadata.TryGetValue("lead_id", out var intentLeadId) || intentLeadId != lead.Id.ToString())
                return Results.UnprocessableEntity(new { error = "Payment does not belong to this renewal." });
            if (intent.AmountInCents != (long)(total * 100))
                return Results.UnprocessableEntity(new { error = "Payment amount does not match the order. Please contact support." });
            // Same lead + same amount is not enough — the intent must be for these exact names.
            if (intent.Metadata.TryGetValue("ids_hash", out var intentIdsHash)
                && intentIdsHash != IdsHash(request.SearchResultIds))
                return Results.UnprocessableEntity(new { error = "Payment does not match the selected business names." });

            // Idempotent: a retry / double-click after the renewals were already recorded
            // just returns the existing ids instead of charging the work twice.
            var alreadyRecorded = await db.StripePayments
                .Where(p => p.PaymentIntentId == intent.PaymentIntentId)
                .Select(p => p.RenewalRequestId)
                .ToListAsync();
            if (alreadyRecorded.Count > 0)
                return Results.Ok(new { renewalIds = alreadyRecorded, total });

            var renewalIds = await UpsertPaidRenewalsAsync(
                db, lead, searchResults, request.RenewalYears, pricePer,
                intent.PaymentIntentId, request.CardholderName, intent.Card);

            try { await leadService.MarkConvertedAsync(lead.Id); } catch { }

            foreach (var rid in renewalIds)
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(rid));

            return Results.Ok(new { renewalIds, total });
        }).WithTags("Wizard");

        app.MapGet("/api/renewals/batch", async (string ids, ApplicationDbContext db) =>
        {
            var idList = (ids ?? "").Split(',').Where(s => Guid.TryParse(s, out _)).Select(Guid.Parse).Take(50).ToArray();
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
            string? source = null,
            string? failedAtStep = null,
            string? errorCategory = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            int page = 1,
            int pageSize = 10) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            // Facet params accept comma-separated multi-values ("Failed,Pending").
            var statusValues = Helpers.ParseEnumList<RenewalStatus>(status);
            var sourceValues = Helpers.ParseEnumList<RenewalSource>(source);
            var initiatedByValues = Helpers.ParseEnumList<SearchInitiator>(initiatedBy);
            var categoryValues = (errorCategory ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // One filter pipeline, with each facet dimension excludable so its own option
            // counts can be computed under the OTHER selections (classic faceted search).
            IQueryable<RenewalRequest> Filtered(bool applyStatus = true, bool applySource = true, bool applyInitiatedBy = true, bool applyCategory = true)
            {
                IQueryable<RenewalRequest> q = db.RenewalRequests.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(abn))
                    q = q.Where(r => r.SearchResult.SearchLog.Abn.Contains(abn));
                if (!string.IsNullOrWhiteSpace(failedAtStep))
                    q = q.Where(r => r.FailedAtStep == failedAtStep);
                if (dateFrom.HasValue)
                    q = q.Where(r => r.InitiatedAt >= dateFrom.Value);
                if (dateTo.HasValue)
                {
                    var dt = dateTo.Value.AddDays(1);
                    q = q.Where(r => r.InitiatedAt < dt);
                }
                if (applyStatus && statusValues.Length > 0)
                    q = q.Where(r => statusValues.Contains(r.Status));
                if (applySource && sourceValues.Length > 0)
                    q = q.Where(r => sourceValues.Contains(r.Source));
                if (applyInitiatedBy && initiatedByValues.Length > 0)
                    q = q.Where(r => initiatedByValues.Contains(r.SearchResult.SearchLog.InitiatedBy));
                if (applyCategory && categoryValues.Length > 0)
                    q = q.Where(r => r.ErrorCategory != null && categoryValues.Contains(r.ErrorCategory));
                return q;
            }

            var query = Filtered()
                .Include(r => r.SearchResult).ThenInclude(s => s.SearchLog)
                .Include(r => r.StripePayment)
                .Include(r => r.Lead);

            var statusFacet = await Filtered(applyStatus: false)
                .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var sourceFacet = await Filtered(applySource: false)
                .GroupBy(r => r.Source).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var initiatedByFacet = await Filtered(applyInitiatedBy: false)
                .GroupBy(r => r.SearchResult.SearchLog.InitiatedBy).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var categoryFacet = await Filtered(applyCategory: false)
                .Where(r => r.ErrorCategory != null)
                .GroupBy(r => r.ErrorCategory).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

            var facets = new
            {
                status = statusFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
                source = sourceFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
                initiatedBy = initiatedByFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key.ToString(), count = f.Count }),
                errorCategory = categoryFacet.OrderByDescending(f => f.Count).Select(f => new { value = f.Key!, count = f.Count }),
            };

            var totalCount = await query.CountAsync();
            var now = DateTime.UtcNow;

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
                    failedAtStep = r.FailedAtStep,
                    errorCategory = r.ErrorCategory,
                    attemptCount = r.AttemptCount,
                    nextRetryAt = r.NextRetryAt,
                    transactionReference = r.TransactionReference,
                    initiatedByLabel = r.SearchResult.SearchLog.InitiatedBy.ToString(),
                    stripePaymentSucceeded = r.StripePayment != null && r.StripePayment.PaymentStatus == "succeeded",
                    cardBrand = r.StripePayment != null ? r.StripePayment.CardBrand : null,
                    cardLast4 = r.StripePayment != null ? r.StripePayment.CardLast4 : null,
                    lead = r.Lead == null ? null : new { id = r.Lead.Id, fullName = r.Lead.FullName, email = r.Lead.Email },
                })
                .ToListAsync();

            // Time the row has been in its CURRENT status (TimeSpan ops aren't translatable in EF,
            // so compute in memory). A terminal row entered its status at CompletedAt; an in-flight
            // row (or a failed one, which has no CompletedAt) has been in status since it was initiated.
            var items = rows.Select(r => new
            {
                r.id, r.abn, r.businessName, r.renewalYears, r.amount, r.status, r.source,
                r.paymentType, r.initiatedAt, r.completedAt, r.email, r.errorMessage, r.failedAtStep,
                r.errorCategory, r.attemptCount, r.nextRetryAt,
                r.transactionReference, r.initiatedByLabel, r.stripePaymentSucceeded,
                r.cardBrand, r.cardLast4, r.lead,
                timeInStatusHours = r.completedAt.HasValue
                    ? Math.Round((now - r.completedAt.Value).TotalHours, 1)
                    : Math.Round((now - r.initiatedAt).TotalHours, 1),
            }).ToList();

            // ─────────── Stats block ───────────
            // Deliberately computed on the UNFILTERED table: these read as global KPIs, and
            // when they silently tracked the grid filter a status=Failed filter made the
            // success rate 0% and revenue $0. Day/month boundaries are AEST — the business
            // (and every recurring job) runs on Sydney time, not UTC.
            var all = db.RenewalRequests.AsNoTracking();
            var aest = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
            var aestNow = TimeZoneInfo.ConvertTimeFromUtc(now, aest);
            var todayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(aestNow.Date, DateTimeKind.Unspecified), aest);
            var yesterdayStart = todayStart.AddDays(-1);
            var fourteenDaysAgo = todayStart.AddDays(-13);
            var monthStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(aestNow.Year, aestNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), aest);
            var thirtyDaysAgo = now.AddDays(-30);
            var staleCutoff = now.AddHours(-2);

            // Success rate over DECIDED outcomes: completed vs failures that are actually
            // final. Timing states (not-due-yet, in-progress recheck, scheduled auto-retry)
            // aren't decisions and used to drag the rate to a meaningless 41%.
            var thirty = await all
                .Where(r => r.InitiatedAt >= thirtyDaysAgo)
                .GroupBy(r => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Completed = g.Count(r => r.Status == RenewalStatus.Completed),
                    RealFailed = g.Count(r => r.Status == RenewalStatus.Failed
                        && r.NextRetryAt == null
                        && r.ErrorCategory != RenewalErrorCategories.NotDueYet
                        && r.ErrorCategory != RenewalErrorCategories.AlreadyInProgress),
                })
                .FirstOrDefaultAsync();
            var total30d = thirty?.Total ?? 0;
            var completed30d = thirty?.Completed ?? 0;
            var decided30d = completed30d + (thirty?.RealFailed ?? 0);
            var successRate30d = decided30d > 0 ? Math.Round((decimal)completed30d * 100m / decided30d, 1) : 0m;

            // Avg completion minutes — the 100 most recent completions; a retried renewal
            // measures from its last attempt, not the original initiation days earlier.
            var completionSamples = await all
                .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null && r.CompletedAt >= thirtyDaysAgo)
                .OrderByDescending(r => r.CompletedAt)
                .Select(r => new { r.InitiatedAt, r.LastAttemptAt, CompletedAt = r.CompletedAt!.Value })
                .Take(100)
                .ToListAsync();
            decimal? avgCompletionMinutes = null;
            if (completionSamples.Count > 0)
            {
                var totalMinutes = completionSamples.Sum(s => (s.CompletedAt - (s.LastAttemptAt ?? s.InitiatedAt)).TotalMinutes);
                avgCompletionMinutes = (decimal)Math.Round(totalMinutes / completionSamples.Count, 1);
            }

            var revenueMtd = await all
                .Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt != null && r.CompletedAt >= monthStart)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;

            // Honest pipeline split. "In flight" is only what actually has a live run;
            // stale rows are surfaced as stuck (the reconciliation sweep repairs them),
            // scheduled retries carry their NextRetryAt, and needs-review is the human queue.
            var pipelineRows = await all
                .Where(r => r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing || r.Status == RenewalStatus.Failed)
                .Select(r => new { r.Status, r.Amount, r.InitiatedAt, r.LastAttemptAt, r.NextRetryAt, r.ErrorCategory })
                .ToListAsync();
            var inflight = pipelineRows.Where(r => r.Status != RenewalStatus.Failed).ToList();
            var liveValue = inflight.Where(r => (r.LastAttemptAt ?? r.InitiatedAt) >= staleCutoff).Sum(r => r.Amount);
            var stuckRows = inflight.Where(r => (r.LastAttemptAt ?? r.InitiatedAt) < staleCutoff).ToList();
            var stuckValue = stuckRows.Sum(r => r.Amount);
            var stuckCount = stuckRows.Count;
            var scheduledRetryValue = pipelineRows
                .Where(r => r.Status == RenewalStatus.Failed && r.NextRetryAt != null)
                .Sum(r => r.Amount);
            var needsReviewRows = pipelineRows
                .Where(r => r.Status == RenewalStatus.Failed && r.NextRetryAt == null && r.ErrorCategory != RenewalErrorCategories.NotDueYet)
                .ToList();
            var needsReviewCount = needsReviewRows.Count;
            var needsReviewValue = needsReviewRows.Sum(r => r.Amount);

            var failedValue30d = await all
                .Where(r => r.Status == RenewalStatus.Failed && r.InitiatedAt >= thirtyDaysAgo)
                .SumAsync(r => (decimal?)r.Amount) ?? 0m;

            var todayCount = await all.CountAsync(r => r.InitiatedAt >= todayStart);
            var yesterdayCount = await all.CountAsync(r => r.InitiatedAt >= yesterdayStart && r.InitiatedAt < todayStart);
            decimal? deltaPct = null;
            if (yesterdayCount > 0)
                deltaPct = Math.Round(((decimal)(todayCount - yesterdayCount) * 100m) / yesterdayCount, 1);

            // AEST day buckets (the timezone shift isn't EF-translatable; bucket in memory).
            var dailyTimes = await all
                .Where(r => r.InitiatedAt >= fourteenDaysAgo)
                .Select(r => r.InitiatedAt)
                .ToListAsync();
            var dailyMap = dailyTimes
                .GroupBy(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t, aest).Date))
                .ToDictionary(g => g.Key, g => g.Count());
            var daily14d = Enumerable.Range(0, 14).Select(i =>
            {
                var date = DateOnly.FromDateTime(aestNow.Date.AddDays(-13 + i));
                return new { date = date.ToString("yyyy-MM-dd"), count = dailyMap.GetValueOrDefault(date, 0) };
            }).ToList();

            // Failure breakdown by category (typed, actionable) — retryable categories are
            // the machine's problem, the rest are the operator's.
            var categoryBreakdown = await all
                .Where(r => r.Status == RenewalStatus.Failed && r.InitiatedAt >= thirtyDaysAgo && r.ErrorCategory != null)
                .GroupBy(r => r.ErrorCategory)
                .Select(g => new { Category = g.Key!, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            return Results.Ok(new
            {
                totalCount,
                page,
                pageSize,
                items,
                facets,
                stats = new
                {
                    successRate30d,
                    completed30d,
                    decided30d,
                    total30d,
                    avgCompletionMinutes,
                    revenueMtd,
                    liveValue,
                    stuckValue,
                    stuckCount,
                    scheduledRetryValue,
                    needsReviewCount,
                    needsReviewValue,
                    failedValue30d,
                    today = todayCount,
                    yesterday = yesterdayCount,
                    deltaPct,
                    daily14d,
                    errorCategoryBreakdown = categoryBreakdown.Select(c => new
                    {
                        category = c.Category,
                        count = c.Count,
                        retryable = c.Category == RenewalErrorCategories.Transient || c.Category == RenewalErrorCategories.AlreadyInProgress,
                    }).ToList(),
                },
            });
        });

        admin.MapPost("/retry-bulk", async (
            ApplicationDbContext db,
            IBackgroundJobClient jobs,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? source = null) =>
        {
            IQueryable<RenewalRequest> q = db.RenewalRequests
                .Include(r => r.StripePayment)
                .Where(r => r.Status == RenewalStatus.Failed);

            if (dateFrom.HasValue) q = q.Where(r => r.InitiatedAt >= dateFrom.Value);
            if (dateTo.HasValue)
            {
                var dt = dateTo.Value.AddDays(1);
                q = q.Where(r => r.InitiatedAt < dt);
            }
            if (!string.IsNullOrWhiteSpace(source) && Enum.TryParse<RenewalSource>(source, true, out var src))
                q = q.Where(r => r.Source == src);

            var candidates = await q.ToListAsync();
            var retried = 0;
            var skipped = 0;
            var skippedPaymentRisk = 0;

            foreach (var renewal in candidates)
            {
                // Same eligibility as per-row /retry: Stripe payments must have succeeded;
                // External renewals are always eligible to retry the ASIC step.
                if (renewal.PaymentType == PaymentType.Stripe &&
                    (renewal.StripePayment is null || renewal.StripePayment.PaymentStatus != "succeeded"))
                {
                    skipped++;
                    continue;
                }

                // A failure after ASIC took payment ("Complete Payment Action", or any run
                // that got far enough to hold a tokenization id) must not be blindly re-run —
                // the retry pays ASIC's card again. These need per-row verification first.
                if (renewal.FailedAtStep == "Complete Payment Action" || renewal.HostedTokenizationId != null)
                {
                    skippedPaymentRisk++;
                    continue;
                }

                // Keep ErrorMessage/FailedAtStep — the next run overwrites them, and until
                // then the operator can still see why the last attempt failed.
                renewal.Status = RenewalStatus.Pending;
                renewal.NextRetryAt = null;
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewal.Id));
                retried++;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { retried, skipped, skippedPaymentRisk });
        });

        // Repairs DB-vs-queue divergence (orphaned Pending rows, stale Processing rows).
        // dryRun defaults to true so a plain POST is always a safe preview.
        admin.MapPost("/reconcile", async (
            IRenewalReconciliationService reconciliation,
            bool dryRun = true,
            int maxRequeue = 25) =>
        {
            var report = await reconciliation.ReconcileAsync(dryRun, Math.Clamp(maxRequeue, 0, 500));
            return Results.Ok(report);
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
                errorCategory = r.ErrorCategory,
                attemptCount = r.AttemptCount,
                lastAttemptAt = r.LastAttemptAt,
                nextRetryAt = r.NextRetryAt,
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
            if (renewal.FailedAtStep == "Complete Payment Action" || renewal.HostedTokenizationId != null)
                return Results.BadRequest(new { error = "Cannot retry automatically: ASIC may have already taken payment for this run. Verify the renewal at ASIC before retrying." });

            // Keep ErrorMessage/FailedAtStep — the next run overwrites them, and until
            // then the operator can still see why the last attempt failed.
            renewal.Status = RenewalStatus.Pending;
            renewal.NextRetryAt = null;
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

            return Results.Ok(new
            {
                renewalId = renewal.Id,
                status = renewal.Status.ToString(),
                message = "Renewal queued. Poll /api/admin/renewals/{id} for the outcome.",
            });
        }).RequireAuthorization().WithTags("Admin.Renewals");
    }

    /// <summary>Stable fingerprint of a set of search-result ids, order-independent.</summary>
    private static string IdsHash(IEnumerable<Guid> ids) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join(",", ids.OrderBy(id => id)))));

    /// <summary>
    /// Records the renewals for a batch that has already been paid — shared by the
    /// immediate-success path and the post-3DS completion endpoint so the two can't drift.
    /// Reuses an existing RenewalRequest per search result (a failed earlier attempt), else creates one.
    /// </summary>
    private static async Task<List<Guid>> UpsertPaidRenewalsAsync(
        ApplicationDbContext db,
        Lead lead,
        List<SearchResult> searchResults,
        int renewalYears,
        decimal pricePer,
        string paymentIntentId,
        string? cardholderName,
        StripeCardInfo? card)
    {
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
                renewal.RenewalYears = renewalYears;
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
                renewal.ErrorCategory = null;
                renewal.NextRetryAt = null;
            }
            else
            {
                renewal = new RenewalRequest
                {
                    Id = Guid.NewGuid(),
                    SearchResultId = sr.Id,
                    LeadId = lead.Id,
                    InitiatedAt = DateTime.UtcNow,
                    RenewalYears = renewalYears,
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

            // Payment rows are never deleted — a reused renewal's row is updated in place.
            // (The batch endpoint refuses names whose payment already succeeded, so this
            // only ever overwrites a non-succeeded prior attempt.)
            if (renewal.StripePayment is not null)
            {
                var payment = renewal.StripePayment;
                payment.PaymentIntentId = paymentIntentId;
                payment.PaymentStatus = "succeeded";
                payment.PaidAt = DateTime.UtcNow;
                payment.CardholderName = cardholderName;
                payment.CardBrand = card?.Brand;
                payment.CardLast4 = card?.Last4;
                payment.CardExpMonth = card?.ExpMonth;
                payment.CardExpYear = card?.ExpYear;
            }
            else
            {
                db.StripePayments.Add(new StripePayment
                {
                    Id = Guid.NewGuid(),
                    RenewalRequestId = renewal.Id,
                    PaymentIntentId = paymentIntentId,
                    PaymentStatus = "succeeded",
                    PaidAt = DateTime.UtcNow,
                    CardholderName = cardholderName,
                    CardBrand = card?.Brand,
                    CardLast4 = card?.Last4,
                    CardExpMonth = card?.ExpMonth,
                    CardExpYear = card?.ExpYear,
                });
            }
            await db.SaveChangesAsync();

            renewalIds.Add(renewal.Id);
        }
        return renewalIds;
    }
}
