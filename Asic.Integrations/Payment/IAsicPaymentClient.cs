
namespace Asic.Integrations.Payment;

public interface IAsicPaymentClient
{
    Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, CreditCardDetails cardDetails);
}