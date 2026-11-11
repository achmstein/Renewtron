using Asic.Integrations.Models;

namespace Asic.Integrations.Abstractions;

public interface IAsicPaymentClient
{
    Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, CreditCardDetails cardDetails);
}