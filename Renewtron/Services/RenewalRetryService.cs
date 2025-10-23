using Asic.Client.Abstractions;
using Asic.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public class RenewalRetryService : IRenewalRetryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAsicRenewalClient _renewalClient;
    private readonly IEmailService _emailService;
    private readonly IOptions<AsicCreditCardSettings> _asicCardSettings;

    public RenewalRetryService(
        ApplicationDbContext dbContext,
        IAsicRenewalClient renewalClient,
        IEmailService emailService,
        IOptions<AsicCreditCardSettings> asicCardSettings)
    {
        _dbContext = dbContext;
        _renewalClient = renewalClient;
        _emailService = emailService;
        _asicCardSettings = asicCardSettings;
    }

    public async Task<(bool success, string message)> RetryRenewalAsync(Guid renewalRequestId)
    {
        try
        {
            // Load the renewal request with related data
            var renewalRequest = await _dbContext.RenewalRequests
                .Include(r => r.SearchResult)
                    .ThenInclude(sr => sr.SearchLog)
                .Include(r => r.StripePayment)
                .FirstOrDefaultAsync(r => r.Id == renewalRequestId);

            if (renewalRequest == null)
            {
                return (false, "Renewal request not found");
            }

            if (renewalRequest.Status == RenewalStatus.Completed)
            {
                return (false, "This renewal has already been completed successfully");
            }

            // Mark as processing
            renewalRequest.Status = RenewalStatus.Processing;
            await _dbContext.SaveChangesAsync();

            // Check if payment was successful (only for Stripe payments)
            if (renewalRequest.PaymentType == PaymentType.Stripe &&
                (renewalRequest.StripePayment == null || renewalRequest.StripePayment.PaymentStatus != "succeeded"))
            {
                return (false, "Cannot retry: Payment was not successful. Customer needs to retry payment.");
            }

            // Get ASIC card details
            var asicCard = _asicCardSettings.Value;
            var asicCardDetails = new CreditCardDetails
            {
                CardNumber = asicCard.CardNumber,
                CardholderName = asicCard.CardholderName,
                ExpiryMonth = asicCard.ExpiryMonth,
                ExpiryYear = asicCard.ExpiryYear,
                Cvc = asicCard.Cvc
            };

            // Retry the ASIC renewal
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

            if (result.IsSuccess && !string.IsNullOrEmpty(renewalRequest.Email))
            {
                // Send confirmation email
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
                }
                catch (Exception emailEx)
                {
                    // Log email error but don't fail the retry
                    Console.WriteLine($"Email send failed during retry: {emailEx.Message}");
                }

                return (true, "Renewal completed successfully!");
            }
            else
            {
                return (false, $"Renewal failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error retrying renewal: {ex.Message}");
        }
    }
}
