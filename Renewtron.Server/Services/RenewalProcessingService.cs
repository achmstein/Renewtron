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
    private readonly ApplicationDbContext _dbContext;
    private readonly IAsicRenewalClient _renewalClient;
    private readonly IEmailService _emailService;
    private readonly IOptions<AsicSettings> _asicSettings;
    private readonly IOntraportSalesService _ontraportSalesService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<RenewalProcessingService> _logger;

    public RenewalProcessingService(
        ApplicationDbContext dbContext,
        IAsicRenewalClient renewalClient,
        IEmailService emailService,
        IOptions<AsicSettings> asicSettings,
        IOntraportSalesService ontraportSalesService,
        IBackgroundJobClient backgroundJobs,
        ILogger<RenewalProcessingService> logger)
    {
        _dbContext = dbContext;
        _renewalClient = renewalClient;
        _emailService = emailService;
        _asicSettings = asicSettings;
        _ontraportSalesService = ontraportSalesService;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
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

            // Mark as processing
            renewalRequest.Status = RenewalStatus.Processing;
            await _dbContext.SaveChangesAsync();

            // Verify payment was successful (either Stripe or external payment)
            if (renewalRequest.PaymentType == PaymentType.Stripe &&
                (renewalRequest.StripePayment == null || renewalRequest.StripePayment.PaymentStatus != "succeeded"))
            {
                _logger.LogError("Renewal request {RenewalRequestId} does not have successful payment", renewalRequestId);
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
                        ontraportSale.Status = result.IsSuccess
                            ? OntraportSaleStatus.RenewalCompleted
                            : OntraportSaleStatus.RenewalFailed;
                        ontraportSale.ProcessedAt = DateTime.UtcNow;
                        ontraportSale.ErrorMessage = result.IsSuccess ? null : result.Message;
                        await _dbContext.SaveChangesAsync();

                        // Sync status back to Ontraport contact
                        await _ontraportSalesService.UpdateOntraportContactStatusAsync(
                            ontraportSale.OntraportContactId, result.IsSuccess, result.TransactionReference);
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

                // Fire-and-forget ATO onboarding for individuals (only when TFN provided)
                if (!string.IsNullOrEmpty(renewalRequest.Tfn) && renewalRequest.DateOfBirth is not null)
                {
                    try
                    {
                        _backgroundJobs.Enqueue<IAtoOnboardingService>(s => s.EnqueueAsync(renewalRequestId));
                    }
                    catch (Exception atoEx)
                    {
                        _logger.LogError(atoEx, "Failed to schedule ATO onboarding for renewal {RenewalRequestId}", renewalRequestId);
                    }
                }

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
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to update error status for renewal request {RenewalRequestId}", renewalRequestId);
            }
        }
    }
}
