using Asic.Client.Abstractions;
using Asic.Client.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public class RenewalProcessingService : IRenewalProcessingService
{
    // Total processing runs allowed per renewal (first attempt + automatic retries).
    private const int MaxAttempts = 4;

    public const string OtpCheckStep = "OTP Check";
    private static readonly TimeSpan OtpCheckRetryDelay = TimeSpan.FromHours(1);
    // After a failed card attempt: another card try waits an hour; switching to BPAY goes soon.
    private static readonly TimeSpan CardRetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan BpayFallbackDelay = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _dbContext;
    private readonly IAsicRenewalClient _renewalClient;
    private readonly ISmsProvider _smsProvider;
    private readonly IEmailService _emailService;
    private readonly IOptionsSnapshot<AsicSettings> _asicSettings;
    private readonly IOntraportSalesService _ontraportSalesService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<RenewalProcessingService> _logger;

    public RenewalProcessingService(
        ApplicationDbContext dbContext,
        IAsicRenewalClient renewalClient,
        ISmsProvider smsProvider,
        IEmailService emailService,
        IOptionsSnapshot<AsicSettings> asicSettings,
        IOntraportSalesService ontraportSalesService,
        IBackgroundJobClient backgroundJobClient,
        ILogger<RenewalProcessingService> logger)
    {
        _dbContext = dbContext;
        _renewalClient = renewalClient;
        _smsProvider = smsProvider;
        _emailService = emailService;
        _asicSettings = asicSettings;
        _ontraportSalesService = ontraportSalesService;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task ProcessRenewalAsync(Guid renewalRequestId)
    {
        try
        {
            _logger.LogInformation("Starting background renewal processing for request {RenewalRequestId}", renewalRequestId);

            // Load the renewal request with related data
            var renewalRequest = await _dbContext.RenewalRequests
                .Include(r => r.SearchResult)
                    .ThenInclude(sr => sr.SearchLog)
                .Include(r => r.StripePayment)
                .FirstOrDefaultAsync(r => r.Id == renewalRequestId);

            if (renewalRequest == null)
            {
                _logger.LogError("Renewal request {RenewalRequestId} not found", renewalRequestId);
                return;
            }

            // Skip if already completed
            if (renewalRequest.Status == RenewalStatus.Completed)
            {
                _logger.LogWarning("Renewal request {RenewalRequestId} already completed", renewalRequestId);
                return;
            }

            // A recent Processing row means another run is (or was moments ago) live —
            // e.g. Hangfire re-delivered after a crash. Never run the ASIC pipeline twice;
            // if the first run actually died, the reconciliation sweep repairs the row.
            if (renewalRequest.Status == RenewalStatus.Processing &&
                renewalRequest.LastAttemptAt.HasValue &&
                renewalRequest.LastAttemptAt.Value > DateTime.UtcNow.AddMinutes(-30))
            {
                _logger.LogWarning("Renewal request {RenewalRequestId} already has a recent run (started {LastAttemptAt:u}); skipping", renewalRequestId, renewalRequest.LastAttemptAt);
                return;
            }

            // Without a readable OTP the card payment can't finish, and the half-done run leaves an
            // unpaid transaction at ASIC that blocks this name for 48 hours. So check first, and if
            // the OTP conversation is broken, hold the renewal (no attempt used) and check again hourly.
            // Paying later by BPAY needs no card, so no OTP either: either BPAY is the method, or
            // this name has had as many failed card attempts as Settings allow.
            var cardFailures = await CardFailuresSoFarAsync(renewalRequest);
            var payLater = UsesBpay(cardFailures);
            if (!renewalRequest.IsTest && !payLater && await _smsProvider.CheckAsync() is { } otpProblem)
            {
                renewalRequest.Status = RenewalStatus.Failed;
                renewalRequest.FailedAtStep = OtpCheckStep;
                renewalRequest.ErrorMessage = $"Not started at ASIC: {otpProblem}";
                renewalRequest.ErrorCategory = RenewalErrorCategories.Transient;
                renewalRequest.NextRetryAt = DateTime.UtcNow + OtpCheckRetryDelay;
                await _dbContext.SaveChangesAsync();
                _backgroundJobClient.Schedule<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewalRequestId), OtpCheckRetryDelay);
                _logger.LogError("Renewal {RenewalRequestId} held before ASIC: {OtpProblem}. Checking again in {Delay}.",
                    renewalRequestId, otpProblem, OtpCheckRetryDelay);
                return;
            }

            // Mark as processing
            renewalRequest.Status = RenewalStatus.Processing;
            renewalRequest.AttemptCount += 1;
            renewalRequest.LastAttemptAt = DateTime.UtcNow;
            renewalRequest.NextRetryAt = null;
            await _dbContext.SaveChangesAsync();

            // Verify payment was successful (either Stripe or external payment)
            if (renewalRequest.PaymentType == PaymentType.Stripe &&
                (renewalRequest.StripePayment == null || renewalRequest.StripePayment.PaymentStatus != "succeeded"))
            {
                _logger.LogError("Renewal request {RenewalRequestId} does not have successful payment", renewalRequestId);
                renewalRequest.Status = RenewalStatus.Failed;
                renewalRequest.FailedAtStep = "StripePayment";
                renewalRequest.ErrorMessage = "Stripe payment has not succeeded for this renewal, so ASIC processing was not started.";
                renewalRequest.ErrorCategory = RenewalErrorCategories.Terminal;
                await _dbContext.SaveChangesAsync();
                return;
            }

            // Get ASIC settings from configuration
            var asicSettings = _asicSettings.Value;
            var asicCardDetails = new CreditCardDetails
            {
                CardNumber = asicSettings.CardNumber,
                CardholderName = asicSettings.CardholderName,
                ExpiryMonth = asicSettings.ExpiryMonth,
                ExpiryYear = asicSettings.ExpiryYear,
                Cvc = asicSettings.Cvc
            };

            _logger.LogInformation("Processing ASIC renewal for {BusinessName} (ABN: {Abn})",
                renewalRequest.SearchResult.BusinessName,
                renewalRequest.SearchResult.SearchLog.Abn);

            // A test customer's renewal never reaches ASIC (or its card): it simply succeeds.
            var result = renewalRequest.IsTest
                ? new RenewalResult
                {
                    IsSuccess = true,
                    Message = "Test mode: not sent to ASIC.",
                    TransactionReference = "TEST-" + Random.Shared.Next(10_000_000, 99_999_999),
                }
                : await _renewalClient.RenewBusinessNameAsync(
                    renewalRequest.SearchResult.SearchLog.Abn,
                    renewalRequest.SearchResult.AccountNumber,
                    renewalRequest.RenewalYears,
                    asicSettings.Email ?? "",
                    asicCardDetails,
                    payLater
                );

            // Update renewal request with result
            renewalRequest.Status = result.IsSuccess ? RenewalStatus.Completed : RenewalStatus.Failed;
            renewalRequest.CompletedAt = result.IsSuccess ? DateTime.UtcNow : null;
            renewalRequest.TransactionReference = result.TransactionReference;
            renewalRequest.HostedTokenizationId = result.HostedTokenizationId;
            if (result.IsSuccess && !string.IsNullOrEmpty(result.BpayReference))
            {
                renewalRequest.BpayBillerCode = result.BpayBillerCode;
                renewalRequest.BpayReference = result.BpayReference;
                renewalRequest.BpayPaidAt = null;
                _logger.LogWarning("Renewal {RenewalRequestId} lodged to pay later: BPAY biller {BillerCode}, reference {Reference}, transaction {TransactionReference}",
                    renewalRequestId, result.BpayBillerCode, result.BpayReference, result.TransactionReference);
            }
            renewalRequest.ErrorMessage = result.IsSuccess ? null : result.Message;
            renewalRequest.FailedAtStep = result.IsSuccess ? null : result.FailedAtStep;
            renewalRequest.ErrorCategory = result.IsSuccess ? null : RenewalErrorClassifier.Classify(result.FailedAtStep, result.Message);

            // A failed card attempt with "switch to BPAY after N" on: try the card again, or once
            // N attempts have failed, pay later by BPAY. Either way the renewal isn't finished, so
            // the customer's Ontraport record isn't told it failed.
            var retryingAfterCardFailure = false;
            if (!result.IsSuccess && !payLater && RenewalErrorClassifier.IsCardFailure(result.FailedAtStep, result.Message))
            {
                renewalRequest.CardFailureCount = cardFailures + 1;
                retryingAfterCardFailure = ScheduleCardFailureRetry(renewalRequest);
            }

            if (!result.IsSuccess && !retryingAfterCardFailure)
                ScheduleAutoRetryIfEligible(renewalRequest);

            await _dbContext.SaveChangesAsync();

            // Update OntraportSale status if this renewal came from Ontraport
            if (retryingAfterCardFailure)
            {
                // Still in progress: the outcome is written when the retry finishes.
            }
            else if (renewalRequest.Source == RenewalSource.Ontraport)
            {
                try
                {
                    var ontraportSale = await _dbContext.OntraportSales
                        .FirstOrDefaultAsync(s => s.RenewalRequestId == renewalRequestId);
                    if (ontraportSale != null)
                    {
                        if (result.IsSuccess)
                        {
                            ontraportSale.Status = OntraportSaleStatus.RenewalCompleted;
                            ontraportSale.ErrorMessage = null;
                        }
                        else
                        {
                            ontraportSale.Status = OntraportSaleStatusClassifier.ClassifyError(result.Message);
                            ontraportSale.ErrorMessage = result.Message;

                            // Retryable states (AsicNotYetDue / RenewalInProgress) need RenewalRequestId
                            // cleared so the next ProcessEligibleRenewalsAsync run picks them up again.
                            // The failed RenewalRequest record stays in the DB for audit but is no longer
                            // linked back from the OntraportSale.
                            if (ontraportSale.Status == OntraportSaleStatus.AsicNotYetDue
                                || ontraportSale.Status == OntraportSaleStatus.RenewalInProgress)
                            {
                                ontraportSale.RenewalRequestId = null;
                            }
                        }
                        ontraportSale.ProcessedAt = DateTime.UtcNow;

                        // ASIC extends from the existing expiry, so the new due date is the old
                        // one plus the term we just bought. Only a completed renewal moves it.
                        DateTime? newRenewalDueDate = null;
                        if (result.IsSuccess)
                        {
                            var basis = ontraportSale.RenewalDueDate ?? DateTime.UtcNow;
                            newRenewalDueDate = basis.AddYears(renewalRequest.RenewalYears);
                            ontraportSale.RenewalDueDate = newRenewalDueDate;
                        }

                        await SyncOutcomeToOntraportAsync(
                            ontraportSale.OntraportContactId,
                            OntraportRenewalOutcomeExtensions.FromSaleStatus(ontraportSale.Status),
                            newRenewalDueDate);
                    }
                }
                catch (Exception opEx)
                {
                    _logger.LogError(opEx, "Failed to update Ontraport sale status for renewal {RenewalRequestId}", renewalRequestId);
                }
            }
            else if (renewalRequest.Source == RenewalSource.Renewtron && renewalRequest.LeadId is { } leadId)
            {
                // Wizard renewal: record the outcome on the customer's Ontraport contact too.
                // The push service works out which contact (never trusting an id off the URL).
                var outcome = result.IsSuccess
                    ? OntraportRenewalOutcome.Successful
                    : OntraportRenewalOutcomeExtensions.FromSaleStatus(OntraportSaleStatusClassifier.ClassifyError(result.Message));
                _backgroundJobClient.Enqueue<IOntraportContactPushService>(s => s.PushRenewalOutcomeAsync(leadId, outcome));
            }

            if (result.IsSuccess)
            {
                _logger.LogInformation("ASIC renewal successful for request {RenewalRequestId}. Transaction: {TransactionRef}",
                    renewalRequestId, result.TransactionReference);

                // Send confirmation email
                if (!string.IsNullOrEmpty(renewalRequest.Email))
                {
                    try
                    {
                        await _emailService.SendRenewalConfirmationAsync(
                            renewalRequest.Email,
                            renewalRequest.SearchResult.BusinessName,
                            renewalRequest.SearchResult.SearchLog.Abn,
                            renewalRequest.RenewalYears,
                            renewalRequest.Amount,
                            result.TransactionReference ?? "N/A"
                        );

                        _logger.LogInformation("Confirmation email sent to {Email} for request {RenewalRequestId}",
                            renewalRequest.Email, renewalRequestId);
                    }
                    catch (Exception emailEx)
                    {
                        _logger.LogError(emailEx, "Failed to send confirmation email for request {RenewalRequestId}", renewalRequestId);
                        // Don't throw - email failure shouldn't fail the entire process
                    }
                }
            }
            else
            {
                _logger.LogError("ASIC renewal failed for request {RenewalRequestId}. Error: {ErrorMessage}, Step: {FailedStep}",
                    renewalRequestId, result.Message, result.FailedAtStep);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing renewal request {RenewalRequestId}", renewalRequestId);

            // Update the renewal request with error information
            try
            {
                var renewalRequest = await _dbContext.RenewalRequests.FindAsync(renewalRequestId);
                if (renewalRequest != null)
                {
                    renewalRequest.Status = RenewalStatus.Failed;
                    renewalRequest.ErrorMessage = $"Background processing error: {ex.Message}";
                    renewalRequest.FailedAtStep = "BackgroundProcessing";
                    renewalRequest.ErrorCategory = RenewalErrorCategories.Transient;
                    ScheduleAutoRetryIfEligible(renewalRequest);
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to update error status for renewal request {RenewalRequestId}", renewalRequestId);
            }
        }
    }

    /// <summary>
    /// Schedules a delayed re-run for retryable failures, with backoff and an attempt cap.
    /// Never retries once a run holds a tokenization id (ASIC may have taken payment),
    /// never retries terminal categories, and leaves Ontraport timing states
    /// (not-due / in-progress) to the daily sale processor, which owns that loop.
    /// </summary>
    /// <summary>
    /// Writes a renewal outcome onto an Ontraport contact (f5481, and f5135 when a due date is
    /// given) through an outbox row: the row is committed first, so if the write fails (or we
    /// crash), the recurring outbox job retries it instead of the contact silently never updating.
    /// </summary>
    private async Task SyncOutcomeToOntraportAsync(string contactId, OntraportRenewalOutcome outcome, DateTime? newRenewalDueDate)
    {
        var outboxEntry = new OntraportSyncOutbox
        {
            Id = Guid.NewGuid(),
            OntraportContactId = contactId,
            Outcome = outcome,
            NewRenewalDueDate = newRenewalDueDate,
            CreatedAt = DateTime.UtcNow,
        };
        _dbContext.OntraportSyncOutbox.Add(outboxEntry);
        await _dbContext.SaveChangesAsync();

        outboxEntry.AttemptCount = 1;
        outboxEntry.LastAttemptAt = DateTime.UtcNow;
        var synced = await _ontraportSalesService.SyncRenewalOutcomeAsync(
            outboxEntry.OntraportContactId, outboxEntry.Outcome, outboxEntry.NewRenewalDueDate);
        if (synced)
            outboxEntry.SentAt = DateTime.UtcNow;
        else
            outboxEntry.LastError = "Immediate sync failed; the outbox job will retry.";
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>Whether this run pays later by BPAY rather than by card.</summary>
    private bool UsesBpay(int cardFailures)
    {
        var settings = _asicSettings.Value;
        if (AsicPaymentMethods.IsBpay(settings.PaymentMethod)) return true;
        var after = settings.BpayAfterCardFailures;
        return after > 0 && cardFailures >= after;
    }

    /// <summary>
    /// Failed card attempts so far for this business name: this record's, or a recent earlier
    /// record's for the same ABN and ASIC account, whichever is higher.
    /// </summary>
    private async Task<int> CardFailuresSoFarAsync(RenewalRequest renewalRequest)
    {
        var abn = renewalRequest.SearchResult.SearchLog.Abn;
        var account = renewalRequest.SearchResult.AccountNumber;
        var since = DateTime.UtcNow.AddDays(-60);
        var earlier = await _dbContext.RenewalRequests
            .Where(r => r.Id != renewalRequest.Id && r.CardFailureCount > 0 && r.InitiatedAt >= since
                     && r.SearchResult.AccountNumber == account && r.SearchResult.SearchLog.Abn == abn)
            .MaxAsync(r => (int?)r.CardFailureCount) ?? 0;
        return Math.Max(renewalRequest.CardFailureCount, earlier);
    }

    /// <summary>
    /// After a failed card attempt, when Settings switch to BPAY after N failures: schedules the
    /// next run, by card again or (once N have failed) by BPAY. False when the switch is off,
    /// leaving the failure to the usual retry rules.
    /// </summary>
    private bool ScheduleCardFailureRetry(RenewalRequest renewalRequest)
    {
        var after = _asicSettings.Value.BpayAfterCardFailures;
        if (after <= 0) return false;

        var toBpay = renewalRequest.CardFailureCount >= after;
        var delay = toBpay ? BpayFallbackDelay : CardRetryDelay;
        renewalRequest.ErrorCategory = RenewalErrorCategories.Transient;
        renewalRequest.NextRetryAt = DateTime.UtcNow + delay;
        var renewalId = renewalRequest.Id;
        _backgroundJobClient.Schedule<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewalId), delay);

        _logger.LogWarning(toBpay
                ? "Renewal {RenewalRequestId}: card failed {Failures} of {Allowed} times; paying later by BPAY in {Delay}"
                : "Renewal {RenewalRequestId}: card failed {Failures} of {Allowed} times; trying the card again in {Delay}",
            renewalId, renewalRequest.CardFailureCount, after, delay);
        return true;
    }

    private void ScheduleAutoRetryIfEligible(RenewalRequest renewalRequest)
    {
        var category = renewalRequest.ErrorCategory;

        var eligible = renewalRequest.HostedTokenizationId == null
            && renewalRequest.AttemptCount < MaxAttempts
            && (category == RenewalErrorCategories.Transient
                || (category == RenewalErrorCategories.AlreadyInProgress && renewalRequest.Source != RenewalSource.Ontraport));

        if (!eligible) return;

        var delay = category == RenewalErrorCategories.AlreadyInProgress
            ? TimeSpan.FromHours(48)
            : renewalRequest.AttemptCount switch
            {
                1 => TimeSpan.FromMinutes(10),
                2 => TimeSpan.FromHours(1),
                _ => TimeSpan.FromHours(6),
            };

        renewalRequest.NextRetryAt = DateTime.UtcNow + delay;
        var renewalId = renewalRequest.Id;
        _backgroundJobClient.Schedule<IRenewalProcessingService>(s => s.ProcessRenewalAsync(renewalId), delay);

        _logger.LogInformation("Scheduled automatic retry {Attempt}/{Max} for renewal {RenewalRequestId} in {Delay} ({Category})",
            renewalRequest.AttemptCount + 1, MaxAttempts, renewalId, delay, category);
    }
}
