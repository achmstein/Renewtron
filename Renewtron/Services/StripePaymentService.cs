using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using Stripe;

namespace Renewtron.Services;

public class StripePaymentService : IStripePaymentService
{
    private readonly StripeSettings _settings;

    public StripePaymentService(IOptions<StripeSettings> settings)
    {
        _settings = settings.Value;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<(bool Success, string? PaymentIntentId, string? ErrorMessage)> CreatePaymentIntentAsync(
        decimal amount,
        string customerEmail,
        string description,
        Dictionary<string, string> metadata)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(amount * 100), // Stripe expects amount in cents
                Currency = "aud",
                Description = description,
                ReceiptEmail = customerEmail,
                Metadata = metadata,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                },
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            return (true, paymentIntent.Id, null);
        }
        catch (StripeException ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> ConfirmPaymentAsync(
        string paymentIntentId,
        string paymentMethodId)
    {
        try
        {
            var service = new PaymentIntentService();
            var options = new PaymentIntentConfirmOptions
            {
                PaymentMethod = paymentMethodId,
            };

            var paymentIntent = await service.ConfirmAsync(paymentIntentId, options);

            if (paymentIntent.Status == "succeeded")
            {
                return (true, null);
            }

            return (false, $"Payment status: {paymentIntent.Status}");
        }
        catch (StripeException ex)
        {
            return (false, ex.Message);
        }
    }
}