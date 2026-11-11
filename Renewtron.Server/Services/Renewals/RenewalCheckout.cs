using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>The wizard's "pay for these names" request (card already tokenised in the browser).</summary>
public record BatchRenewalRequest(
    Guid LeadId,
    Guid[] SearchResultIds,
    int RenewalYears,
    string PaymentMethodId,
    string? CardholderName);

/// <summary>Second leg after 3D Secure: the browser finished the challenge for this intent.</summary>
public record BatchRenewalCompleteRequest(
    Guid LeadId,
    Guid[] SearchResultIds,
    int RenewalYears,
    string PaymentIntentId,
    string? CardholderName);

/// <summary>What a checkout came to. The endpoint turns each shape into its HTTP response.</summary>
public abstract record CheckoutOutcome
{
    /// <summary>Paid and recorded: the renewals are queued.</summary>
    public sealed record Paid(IReadOnlyList<Guid> RenewalIds, decimal Total, string? PortalSignInUrl) : CheckoutOutcome;

    /// <summary>The card wants 3D Secure; nothing is recorded until /complete.</summary>
    public sealed record NeedsAction(decimal Total, string? ClientSecret) : CheckoutOutcome;

    /// <summary>Refused, with the HTTP status and the customer-facing message.</summary>
    public sealed record Refused(int StatusCode, string Error, string? Code = null) : CheckoutOutcome;
}

/// <summary>
/// Takes payment for renewals from the wizard and records them. The money path: a double-submit
/// must never charge twice, and a paid intent must never be redirected to other names.
/// </summary>
public sealed class RenewalCheckout(
    ApplicationDbContext db,
    IStripePaymentService stripe,
    IBackgroundJobClient jobs,
    ILeadService leadService,
    IOptionsSnapshot<PricingSettings> pricing,
    IOptionsSnapshot<PortalSettings> portal,
    IOptionsSnapshot<AsicSettings> asicSettings,
    IOptionsSnapshot<TestModeSettings> testMode)
{
    private static CheckoutOutcome.Refused BadRequest(string error) => new(StatusCodes.Status400BadRequest, error);
    private static CheckoutOutcome.Refused NotFound(string error) => new(StatusCodes.Status404NotFound, error);
    private static CheckoutOutcome.Refused Conflict(string error, string? code = null) => new(StatusCodes.Status409Conflict, error, code);
    private static CheckoutOutcome.Refused Unprocessable(string error) => new(StatusCodes.Status422UnprocessableEntity, error);

    public async Task<CheckoutOutcome> PayAsync(BatchRenewalRequest request)
    {
        if (ValidateShape(request.RenewalYears, request.SearchResultIds) is { } bad) return bad;

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId);
        if (CheckLead(lead) is { } leadProblem) return leadProblem;

        var searchResults = await LoadSearchResultsAsync(request.SearchResultIds);
        if (searchResults.Count == 0) return NotFound("Search results not found.");

        // Names listed from our copy of the register are only payable once ASIC has
        // confirmed them (account numbers in, names it won't renew struck out).
        if (!asicSettings.Value.ForceFallback && searchResults.Any(sr => !sr.SearchLog.IsVerified))
            return Conflict("We're still confirming your business names with ASIC. Please wait a moment and try again.", "verifying");
        if (searchResults.Any(sr => !sr.IsAvailable))
            return Conflict("ASIC can't renew one or more of the selected business names right now. Please go back and review your selection.", "unavailable");

        // A double-submit (double-click, client retry) must never charge twice: refuse
        // names that already have a succeeded payment, and pin the Stripe call with an
        // idempotency key derived from the batch's identity.
        var resultIds = searchResults.Select(sr => sr.Id).ToList();
        var alreadyPaid = await db.StripePayments
            .Where(p => p.PaymentStatus == StripePaymentStatuses.Succeeded)
            .Where(p => db.RenewalRequests.Any(r => r.Id == p.RenewalRequestId && resultIds.Contains(r.SearchResultId)))
            .AnyAsync();
        if (alreadyPaid)
            return Conflict("One or more of these business names has already been paid for. Refresh the page to see the current status.");

        var pricePer = pricing.Value.GetCustomerPrice(request.RenewalYears);
        var total = pricePer * searchResults.Count;
        var businessNames = string.Join(", ", searchResults.Select(s => s.BusinessName));

        var confirm = await stripe.ConfirmPaymentAsync(
            total,
            lead!.Email,
            $"Business Name Renewal - {searchResults.Count} renewal(s)",
            new Dictionary<string, string>
            {
                ["renewal_count"] = searchResults.Count.ToString(),
                ["business_names"] = businessNames.Length > 500 ? businessNames[..500] : businessNames,
                ["renewal_years"] = request.RenewalYears.ToString(),
                ["lead_id"] = lead.Id.ToString(),
                // Pins the intent to the exact names being paid for; CompleteAsync
                // verifies it so a paid intent can't be redirected to different names.
                ["ids_hash"] = IdsHash(resultIds),
            },
            request.PaymentMethodId,
            IdempotencyKey(lead, request.RenewalYears, resultIds, total),
            testMode: lead.IsTest);

        // Card wants 3D Secure. Nothing is recorded yet: the browser runs
        // stripe.handleNextAction(clientSecret) and then calls CompleteAsync.
        if (confirm.RequiresAction) return new CheckoutOutcome.NeedsAction(total, confirm.ClientSecret);
        if (!confirm.Success) return Unprocessable(confirm.ErrorMessage ?? "Payment failed.");

        var renewalIds = await RecordPaidAsync(lead, searchResults, request.RenewalYears, pricePer,
            confirm.PaymentIntentId!, request.CardholderName, confirm.Card);
        return new CheckoutOutcome.Paid(renewalIds, total, SignInUrl(lead));
    }

    /// <summary>
    /// After 3D Secure: verify the PaymentIntent with Stripe ourselves (never trust the client's
    /// word) and only then record the renewals. Safe to repeat.
    /// </summary>
    public async Task<CheckoutOutcome> CompleteAsync(BatchRenewalCompleteRequest request)
    {
        if (ValidateShape(request.RenewalYears, request.SearchResultIds) is { } bad) return bad;
        if (string.IsNullOrWhiteSpace(request.PaymentIntentId)) return BadRequest("PaymentIntentId is required.");

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId);
        if (CheckLead(lead) is { } leadProblem) return leadProblem;

        var searchResults = await LoadSearchResultsAsync(request.SearchResultIds);
        if (searchResults.Count == 0) return NotFound("Search results not found.");

        var pricePer = pricing.Value.GetCustomerPrice(request.RenewalYears);
        var total = pricePer * searchResults.Count;

        var intent = await stripe.GetPaymentIntentAsync(request.PaymentIntentId, testMode: lead!.IsTest);
        if (intent is null)
            return Unprocessable("Payment could not be verified. Please contact support.");
        if (intent.Status != StripePaymentStatuses.Succeeded)
            return Unprocessable($"Payment was not completed (status: {intent.Status}). You have not been charged — please try again.");
        if (!intent.Metadata.TryGetValue("lead_id", out var intentLeadId) || intentLeadId != lead.Id.ToString())
            return Unprocessable("Payment does not belong to this renewal.");
        if (intent.AmountInCents != (long)(total * 100))
            return Unprocessable("Payment amount does not match the order. Please contact support.");
        // Same lead + same amount is not enough — the intent must be for these exact names.
        if (intent.Metadata.TryGetValue("ids_hash", out var intentIdsHash) && intentIdsHash != IdsHash(request.SearchResultIds))
            return Unprocessable("Payment does not match the selected business names.");

        // Idempotent: a retry / double-click after the renewals were already recorded
        // just returns the existing ids instead of recording the work twice.
        var alreadyRecorded = await db.StripePayments
            .Where(p => p.PaymentIntentId == intent.PaymentIntentId)
            .Select(p => p.RenewalRequestId)
            .ToListAsync();
        if (alreadyRecorded.Count > 0) return new CheckoutOutcome.Paid(alreadyRecorded, total, SignInUrl(lead));

        var renewalIds = await RecordPaidAsync(lead, searchResults, request.RenewalYears, pricePer,
            intent.PaymentIntentId, request.CardholderName, intent.Card);
        return new CheckoutOutcome.Paid(renewalIds, total, SignInUrl(lead));
    }

    private static CheckoutOutcome.Refused? ValidateShape(int renewalYears, Guid[] searchResultIds)
    {
        if (renewalYears != 1 && renewalYears != 3) return BadRequest("RenewalYears must be 1 or 3.");
        if (searchResultIds.Length == 0) return BadRequest("At least one business name is required.");
        return null;
    }

    private CheckoutOutcome.Refused? CheckLead(Lead? lead)
    {
        if (lead is null) return NotFound("Lead not found.");
        if (lead.IsTest && !testMode.Value.StripeReady)
            return Conflict("Test mode is missing its Stripe test keys (Admin → Settings → Test mode).");
        return null;
    }

    private Task<List<SearchResult>> LoadSearchResultsAsync(Guid[] ids) =>
        db.SearchResults
            .Include(sr => sr.SearchLog)
            .Where(sr => ids.Contains(sr.Id))
            .OrderBy(sr => sr.Id)
            .ToListAsync();

    private string? SignInUrl(Lead lead) =>
        // Signs the customer straight into the Business Portal from the confirmation page.
        portal.Value.CheckoutSignInUrl(lead.Email, DateTimeOffset.UtcNow, lead.CreatedAt);

    /// <summary>Stable fingerprint of a set of search-result ids, order-independent.</summary>
    public static string IdsHash(IEnumerable<Guid> ids) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ids.OrderBy(id => id)))));

    /// <summary>
    /// Everything sent to Stripe when the intent is created must be in the key, or a retry after a
    /// price or email change hits Stripe's "same key, different parameters" error.
    /// </summary>
    public static string IdempotencyKey(Lead lead, int renewalYears, IEnumerable<Guid> resultIds, decimal total)
    {
        var source = $"batch:{lead.Id}:{renewalYears}:{string.Join(",", resultIds.OrderBy(id => id))}:{total}:{lead.Email}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    /// <summary>
    /// Records a paid batch and queues the follow-on work. Shared by both legs so they can't drift.
    /// Reuses an existing RenewalRequest per search result (a failed earlier attempt), else creates one.
    /// </summary>
    private async Task<List<Guid>> RecordPaidAsync(
        Lead lead, List<SearchResult> searchResults, int renewalYears, decimal pricePer,
        string paymentIntentId, string? cardholderName, StripeCardInfo? card)
    {
        var renewalIds = new List<Guid>();
        foreach (var sr in searchResults)
        {
            var renewal = await db.RenewalRequests
                .Include(r => r.StripePayment)
                .FirstOrDefaultAsync(r => r.SearchResultId == sr.Id);

            if (renewal is null)
            {
                renewal = new RenewalRequest
                {
                    Id = Guid.NewGuid(),
                    SearchResultId = sr.Id,
                    InitiatedAt = DateTime.UtcNow,
                    Source = RenewalSource.Renewtron,
                };
                db.RenewalRequests.Add(renewal);
            }
            else
            {
                renewal.CompletedAt = null;
                renewal.TransactionReference = null;
                renewal.HostedTokenizationId = null;
                renewal.ErrorMessage = null;
                renewal.FailedAtStep = null;
                renewal.ErrorCategory = null;
                renewal.NextRetryAt = null;
            }
            renewal.LeadId = lead.Id;
            renewal.RenewalYears = renewalYears;
            renewal.Email = lead.Email;
            renewal.MobileNumber = lead.MobileNumber;
            renewal.DateOfBirth = lead.DateOfBirth;
            renewal.Tfn = lead.Tfn;
            renewal.PaymentType = PaymentType.Stripe;
            renewal.IsTest = lead.IsTest;
            renewal.Amount = pricePer;
            renewal.Status = RenewalStatus.Pending;

            // Paid before ASIC will take it: the renewal waits, and the morning check lodges it
            // once ASIC lists the name (EarlyRenewalService).
            if (sr.PayEarly)
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time")));
                var knownDue = await AsicRegistry.AsicDueDateService.KnownDueDateAsync(db, lead.Abn, sr.BusinessName);
                EarlyRenewalService.MarkWaiting(renewal, EarlyRenewalService.NextCheck(today, sr.RegistrationDate, knownDue));
            }
            await db.SaveChangesAsync();

            // Payment rows are never deleted — a reused renewal's row is updated in place.
            // (PayAsync refuses names whose payment already succeeded, so this only ever
            // overwrites a non-succeeded prior attempt.)
            var payment = renewal.StripePayment;
            if (payment is null)
            {
                payment = new StripePayment { Id = Guid.NewGuid(), RenewalRequestId = renewal.Id };
                db.StripePayments.Add(payment);
            }
            payment.PaymentIntentId = paymentIntentId;
            payment.PaymentStatus = StripePaymentStatuses.Succeeded;
            payment.PaidAt = DateTime.UtcNow;
            payment.CardholderName = cardholderName;
            payment.CardBrand = card?.Brand;
            payment.CardLast4 = card?.Last4;
            payment.CardExpMonth = card?.ExpMonth;
            payment.CardExpYear = card?.ExpYear;
            await db.SaveChangesAsync();

            renewalIds.Add(renewal.Id);
        }

        try { await leadService.MarkConvertedAsync(lead.Id); } catch { /* a conversion flag never fails a paid order */ }
        jobs.Enqueue<IOntraportContactPushService>(s => s.PushPaymentAsync(lead.Id, renewalYears));
        foreach (var rid in renewalIds)
            jobs.Enqueue<IRenewalProcessingService>(s => s.ProcessRenewalAsync(rid));
        RenewalOrderService.EnqueueRefresh(jobs);
        EnqueuePaidEarlyEmail(lead, searchResults, renewalYears, pricePer);
        return renewalIds;
    }

    /// <summary>
    /// Names paid for before ASIC takes them: one email telling the customer we have the payment
    /// and will lodge the renewal when ASIC opens it. The renewal site itself doesn't say.
    /// </summary>
    private void EnqueuePaidEarlyEmail(Lead lead, List<SearchResult> searchResults, int renewalYears, decimal pricePer)
    {
        var early = searchResults.Where(sr => sr.PayEarly).Select(sr => sr.BusinessName).ToArray();
        if (early.Length == 0 || string.IsNullOrWhiteSpace(lead.Email)) return;
        var email = lead.Email;
        var abn = lead.Abn;
        var amount = pricePer * early.Length;
        var leadId = (Guid?)lead.Id;
        jobs.Enqueue<IEmailService>(e => e.SendPaidEarlyAsync(email, abn, early, renewalYears, amount, leadId));
    }
}
