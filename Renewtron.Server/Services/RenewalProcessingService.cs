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

    private readonly ApplicationDbContext _dbContext;
    private readonly IAsicRenewalClient _renewalClient;
    private readonly IEmailService _emailService;
    private readonly IOptionsSnapshot<AsicSettings> _asicSettings;
    private readonly IOntraportSalesService _ontraportSalesService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<RenewalProcessingService> _logger;

    public RenewalProcessingService(
        ApplicationDbContext dbContext,
        IAsicRenewalClient renewalClient,
        IEmailService emailService,
        IOptionsSnapshot<AsicSettings> asicSettings,
        IOntraportSalesService ontraportSalesService,
        IBackgroundJobClient backgroundJobClient,
        ILogger<RenewalProcessingService> logger)
    {
        _dbContext = dbContext;
        _renewalClient = renewalClient;
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

            // Process the ASIC renewal using settings email
            var result = await _renewalClient.RenewBusinessNameAsync(
                renewalRequest.SearchResult.SearchLog.Abn,
                renewalRequest.SearchResult.AccountNumber,
                renewalRequest.RenewalYears,
                asicSettings.Email ?? "",
                asicCardDetails
            );

            // Update renewal request with result
            renewalRequest.Status = result.IsSuccess ? RenewalStatus.Completed : RenewalStatus.Failed;
            renewalRequest.CompletedAt = result.IsSuccess ? DateTime.UtcNow : null;
            renewalRequest.TransactionReference = result.TransactionReference;
            renewalRequest.HostedTokenizationId = result.HostedTokenizationId;
            renewalRequest.ErrorMessage = result.IsSuccess ? null : result.Message;
            renewalRequest.FailedAtStep = result.IsSuccess ? null : result.FailedAtStep;
            renewalRequest.ErrorCategory = result.IsSuccess ? null : RenewalErrorClassifier.Classify(result.FailedAtStep, result.Message);

            if (!result.IsSuccess)
                ScheduleAutoRetryIfEligible(renewalRequest);

            await _dbContext.SaveChangesAsync();

            // Update OntraportSale status if this renewal came from Ontraport
            if (renewalRequest.Source == RenewalSource.Ontraport)
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

                        // Sync the outcome back onto the Ontraport contact (f5481, and f5135 on
                        // success) through an outbox row: the row is committed first, so if the
                        // write fails (or we crash), the recurring outbox job retries it instead
                        // of the contact's due date silently never advancing.
                        var outboxEntry = new OntraportSyncOutbox
                        {
                            Id = Guid.NewGuid(),
                            OntraportContactId = ontraportSale.OntraportContactId,
                            Outcome = OntraportRenewalOutcomeExtensions.FromSaleStatus(ontraportSale.Status),
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
                }
                catch (Exception opEx)
                {
                    _logger.LogError(opEx, "Failed to update Ontraport sale status for renewal {RenewalRequestId}", renewalRequestId);
                }
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
