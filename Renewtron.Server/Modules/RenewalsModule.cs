using System.Text;
using Carter;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Services;
using Renewtron.Settings;
using Asic.Integrations.Renewal;

namespace Renewtron.Modules;

public sealed class RenewalsModule : ICarterModule
{

    /// <summary>Query string of GET /api/admin/renewals (all optional).</summary>
    public sealed class RenewalListRequest
    {
        public string? Abn { get; set; }
        public string? Status { get; set; }
        public string? InitiatedBy { get; set; }
        public string? Source { get; set; }
        public string? FailedAtStep { get; set; }
        public string? ErrorCategory { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? View { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }

        public RenewalListQuery ToQuery() => new(Abn, Status, InitiatedBy, Source, FailedAtStep, ErrorCategory,
            DateFrom, DateTo, View, Page ?? 1, PageSize ?? 10);
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // The original single-name charge confirmed a card PaymentIntent with no 3DS leg.
        // The wizard moved to /api/renewals/batch (+ /batch/complete for 3DS); nothing calls
        // this any more, and leaving a public card-charging endpoint around is only risk.
        app.MapPost("/api/renewals", () =>
            Results.Json(new { error = "Use /api/renewals/batch" }, statusCode: StatusCodes.Status410Gone))
            .WithTags("Wizard");

        app.MapPost("/api/renewals/batch", async (BatchRenewalRequest request, RenewalCheckout checkout) =>
            ToResult(await checkout.PayAsync(request), includePaymentFields: true)).WithTags("Wizard");

        // Second leg of the 3DS flow: the browser has finished the challenge.
        app.MapPost("/api/renewals/batch/complete", async (BatchRenewalCompleteRequest request, RenewalCheckout checkout) =>
            ToResult(await checkout.CompleteAsync(request), includePaymentFields: false)).WithTags("Wizard");

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
                // Paid early and waiting for ASIC reads as received: the customer is never told
                // the name isn't due (David: it puts people off paying).
                status = EarlyRenewalService.IsWaiting(r.Status, r.ErrorCategory, r.NextRetryAt) ? "Received" : r.Status.ToString(),
                amount = r.Amount,
                renewalYears = r.RenewalYears,
                businessName = r.SearchResult?.BusinessName,
                accountNumber = r.SearchResult?.AccountNumber,
                transactionReference = r.TransactionReference,
                // Public endpoint: the customer-safe explanation, never ASIC's raw error text.
                customerMessage = RenewalErrorClassifier.CustomerMessage(r.Status, r.ErrorCategory, r.NextRetryAt),
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
                status = RenewalErrorClassifier.CustomerStatus(renewal.Status, renewal.ErrorCategory, renewal.NextRetryAt),
                businessName = renewal.SearchResult.BusinessName,
                abn = renewal.SearchResult.SearchLog.Abn,
                renewalYears = renewal.RenewalYears,
                amount = renewal.Amount,
                completedAt = renewal.CompletedAt,
                transactionReference = renewal.TransactionReference,
                customerMessage = RenewalErrorClassifier.CustomerMessage(renewal.Status, renewal.ErrorCategory, renewal.NextRetryAt),
            });
        }).WithTags("Wizard");

        var admin = app.MapGroup("/api/admin/renewals").RequireAuthorization().WithTags("Admin.Renewals")
            .AddEndpointFilter<RenewalOrderRefreshFilter>();

        admin.MapGet("/", async ([AsParameters] RenewalListRequest request, ApplicationDbContext db) =>
        {
            var list = await RenewalAdminQueries.ListAsync(db, request.ToQuery());
            var stats = await RenewalAdminQueries.StatsAsync(db);
            return Results.Ok(new { totalCount = list.TotalCount, page = list.Page, pageSize = list.PageSize, items = list.Items, facets = list.Facets, stats });
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
                .Where(r => r.Status == RenewalStatus.Failed && r.HandledAt == null);

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
                switch (RenewalRetry.Check(renewal))
                {
                    case RetryBlock.MayHoldPayment: skippedPaymentRisk++; continue;
                    case not null: skipped++; continue;
                }
                RenewalRetry.Requeue(renewal);
                jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewal.Id));
                retried++;
            }

            if (retried > 0) RenewalOrderService.EnqueueRefresh(jobs);
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

            // Where the renewal sits: whose it is, the business name, and the records either side
            // of it (the wizard search before, the Ontraport sale that paid for it).
            var client = r.ClientId is { } clientId
                ? await db.Clients.AsNoTracking().Where(c => c.Id == clientId)
                    .Select(c => new { id = c.Id, fullName = c.FullName }).FirstOrDefaultAsync()
                : null;
            var sale = await db.OntraportSales.AsNoTracking().Where(s => s.RenewalRequestId == r.Id)
                .Select(s => new { id = s.Id, syncedAt = s.SyncedAt, amountPaid = s.AmountPaid, renewalDueDate = s.RenewalDueDate })
                .FirstOrDefaultAsync();
            var order = r.RenewalOrderId is { } oid
                ? await db.RenewalOrders.AsNoTracking().Where(o => o.Id == oid)
                    .Select(o => new { id = o.Id, stage = o.Stage.ToString(), reason = o.Reason.HasValue ? o.Reason.Value.ToString() : null, paidAt = o.PaidAt })
                    .FirstOrDefaultAsync()
                : null;
            var nameDue = r.ClientBusinessNameId is { } nameId
                ? await db.ClientBusinessNames.AsNoTracking().Where(n => n.Id == nameId).Select(n => n.RenewalDueDate).FirstOrDefaultAsync()
                : null;

            return Results.Ok(new
            {
                order,
                id = r.Id,
                client,
                clientBusinessNameId = r.ClientBusinessNameId,
                searchLogId = r.SearchResult.SearchLogId,
                searchedAt = r.SearchResult.SearchLog.SearchedAt,
                sale,
                dueDate = nameDue ?? (sale?.renewalDueDate is { } d ? DateOnly.FromDateTime(d) : null),
                stuck = (r.Status == RenewalStatus.Pending || r.Status == RenewalStatus.Processing)
                    && (r.LastAttemptAt ?? r.InitiatedAt) < DateTime.UtcNow - WorkQueues.StuckAfter,
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
                bpayBillerCode = r.BpayBillerCode,
                bpayReference = r.BpayReference,
                bpayPaidAt = r.BpayPaidAt,
                errorMessage = r.ErrorMessage,
                failedAtStep = r.FailedAtStep,
                errorCategory = r.ErrorCategory,
                attemptCount = r.AttemptCount,
                cardFailureCount = r.CardFailureCount,
                lastAttemptAt = r.LastAttemptAt,
                nextRetryAt = r.NextRetryAt,
                handledAt = r.HandledAt,
                handledNote = r.HandledNote,
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

        // A renewal lodged to pay later: staff record that the BPAY has been paid at the bank
        // (paid = false undoes it).
        admin.MapPost("/{id:guid}/bpay-paid", async (Guid id, bool? paid, ApplicationDbContext db) =>
        {
            var renewal = await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (renewal is null) return Results.NotFound(new { error = "Renewal request not found" });
            if (string.IsNullOrEmpty(renewal.BpayReference))
                return Results.BadRequest(new { error = "This renewal wasn't lodged to pay by BPAY." });
            renewal.BpayPaidAt = paid == false ? null : DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { bpayPaidAt = renewal.BpayPaidAt });
        });

        // Puts back on the work queues a renewal the old-records cleanup marked handled.
        admin.MapDelete("/{id:guid}/handled", async (Guid id, ApplicationDbContext db) =>
        {
            var renewal = await db.RenewalRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (renewal is null) return Results.NotFound(new { error = "Renewal request not found" });
            renewal.HandledAt = null;
            renewal.HandledNote = null;
            await db.SaveChangesAsync();
            return Results.Ok(new { handledAt = (DateTime?)null });
        });

        admin.MapPost("/{id:guid}/retry", async (Guid id, ApplicationDbContext db, IBackgroundJobClient jobs) =>
        {
            var renewal = await db.RenewalRequests
                .Include(r => r.StripePayment)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (renewal is null) return Results.NotFound(new { error = "Renewal request not found" });
            switch (RenewalRetry.Check(renewal))
            {
                case RetryBlock.AlreadyCompleted:
                    return Results.BadRequest(new { error = "This renewal has already been completed successfully" });
                case RetryBlock.NotPaid:
                    return Results.BadRequest(new { error = "Cannot retry: Payment was not successful. Customer needs to retry payment." });
                case RetryBlock.MayHoldPayment:
                    return Results.BadRequest(new { error = "Cannot retry automatically: ASIC may have already taken payment for this run. Verify the renewal at ASIC before retrying." });
            }

            RenewalRetry.Requeue(renewal);
            await db.SaveChangesAsync();

            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(id));
            return Results.Ok(new { message = "Renewal has been queued for retry and will be processed shortly." });
        });
    }

    /// <summary>The wire shapes the wizard has always received for each checkout outcome.</summary>
    private static IResult ToResult(CheckoutOutcome outcome, bool includePaymentFields) => outcome switch
    {
        CheckoutOutcome.NeedsAction a => Results.Ok(new
        {
            renewalIds = Array.Empty<Guid>(),
            total = a.Total,
            requiresAction = true,
            clientSecret = a.ClientSecret,
        }),
        CheckoutOutcome.Paid p when includePaymentFields => Results.Ok(new
        {
            renewalIds = p.RenewalIds, total = p.Total, requiresAction = false, clientSecret = (string?)null,
            portalSignInUrl = p.PortalSignInUrl,
        }),
        CheckoutOutcome.Paid p => Results.Ok(new { renewalIds = p.RenewalIds, total = p.Total, portalSignInUrl = p.PortalSignInUrl }),
        CheckoutOutcome.Refused { Code: { } code } r => Results.Json(new { error = r.Error, code }, statusCode: r.StatusCode),
        CheckoutOutcome.Refused r => Results.Json(new { error = r.Error }, statusCode: r.StatusCode),
        _ => throw new InvalidOperationException($"Unhandled checkout outcome {outcome.GetType().Name}"),
    };
}
