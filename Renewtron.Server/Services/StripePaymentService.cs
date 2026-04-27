using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using Stripe;

namespace Renewtron.Services;

public class StripePaymentService : IStripePaymentService
{
    private readonly StripeSettings _settings;

    public StripePaymentService(IOptionsSnapshot<StripeSettings> settings)
    {
        _settings = settings.Value;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<(bool Success, string? PaymentIntentId, string? ErrorMessage)> ConfirmPaymentAsync(
        decimal amount,
        string customerEmail,
        string description,
        Dictionary<string, string> metadata,
        string paymentMethodId)
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
                PaymentMethod = paymentMethodId,
                Confirm = true,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never",
                },
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            if (paymentIntent.Status == "succeeded")
            {
                return (true, paymentIntent.Id, null);
            }

            return (false, paymentIntent.Id, $"Payment status: {paymentIntent.Status}");
        }
        catch (StripeException ex)
        {
            return (false, null, ex.Message);
        }
    }
}
