using Asic.Client.Abstractions;
using Asic.Client.Models;
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
    private readonly IOptions<AsicCreditCardSettings> _asicCardSettings;
    private readonly ILogger<RenewalProcessingService> _logger;

    public RenewalProcessingService(
        ApplicationDbContext dbContext,
        IAsicRenewalClient renewalClient,
        IEmailService emailService,
        IOptions<AsicCreditCardSettings> asicCardSettings,
        ILogger<RenewalProcessingService> logger)
    {
        _dbContext = dbContext;
        _renewalClient = renewalClient;
        _emailService = emailService;
        _asicCardSettings = asicCardSettings;
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

            // Get ASIC card details from configuration
            var asicCard = _asicCardSettings.Value;
            var asicCardDetails = new CreditCardDetails
            {
                CardNumber = asicCard.CardNumber,
                CardholderName = asicCard.CardholderName,
                ExpiryMonth = asicCard.ExpiryMonth,
                ExpiryYear = asicCard.ExpiryYear,
                Cvc = asicCard.Cvc
            };

            _logger.LogInformation("Processing ASIC renewal for {BusinessName} (ABN: {Abn})",
                renewalRequest.SearchResult.BusinessName,
                renewalRequest.SearchResult.SearchLog.Abn);

            // Process the ASIC renewal
            var result = await _renewalClient.RenewBusinessNameAsync(
                renewalRequest.SearchResult.SearchLog.Abn,
                renewalRequest.SearchResult.BusinessName,
                renewalRequest.RenewalYears,
                renewalRequest.Email ?? "",
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
