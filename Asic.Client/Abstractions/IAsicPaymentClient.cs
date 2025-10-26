using Asic.Client.Models;

namespace Asic.Client.Abstractions;

public interface IAsicPaymentClient
{
    Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, CreditCardDetails cardDetails);
}