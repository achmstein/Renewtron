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

    public async Task<StripeConfirmResult> ConfirmPaymentAsync(
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
                Expand = ["latest_charge"],
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            return paymentIntent.Status switch
            {
                "succeeded" => new StripeConfirmResult(
                    Success: true, RequiresAction: false,
                    paymentIntent.Id, paymentIntent.ClientSecret,
                    CardOf(paymentIntent), ErrorMessage: null),

                // Issuer wants 3D Secure. Not a decline — the browser must run the
                // challenge via stripe.handleNextAction(clientSecret), then the client
                // calls the completion endpoint to record the renewal.
                "requires_action" => new StripeConfirmResult(
                    Success: false, RequiresAction: true,
                    paymentIntent.Id, paymentIntent.ClientSecret,
                    Card: null, ErrorMessage: null),

                _ => new StripeConfirmResult(
                    Success: false, RequiresAction: false,
                    paymentIntent.Id, paymentIntent.ClientSecret,
                    Card: null, ErrorMessage: $"Payment status: {paymentIntent.Status}"),
            };
        }
        catch (StripeException ex)
        {
            return new StripeConfirmResult(false, false, null, null, null, ex.Message);
        }
    }

    public async Task<StripeIntentState?> GetPaymentIntentAsync(string paymentIntentId)
    {
        try
        {
            var service = new PaymentIntentService();
            var paymentIntent = await service.GetAsync(paymentIntentId, new PaymentIntentGetOptions
            {
                Expand = ["latest_charge"],
            });
            return new StripeIntentState(
                paymentIntent.Id,
                paymentIntent.Status,
                paymentIntent.Amount,
                paymentIntent.Metadata ?? new Dictionary<string, string>(),
                CardOf(paymentIntent));
        }
        catch (StripeException)
        {
            return null;
        }
    }

    private static StripeCardInfo? CardOf(PaymentIntent paymentIntent)
    {
        var card = paymentIntent.LatestCharge?.PaymentMethodDetails?.Card;
        if (card is null) return null;
        return new StripeCardInfo(card.Brand, card.Last4, card.ExpMonth.ToString(), card.ExpYear.ToString());
    }
}
