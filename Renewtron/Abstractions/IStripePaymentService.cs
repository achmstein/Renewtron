namespace Renewtron.Abstractions;

public interface IStripePaymentService
{
    Task<(bool Success, string PaymentIntentId, string ErrorMessage)> CreatePaymentIntentAsync(
        decimal amount,
        string customerEmail,
        string description,
        Dictionary<string, string> metadata);

    Task<(bool Success, string ErrorMessage)> ConfirmPaymentAsync(
        string paymentIntentId,
        string paymentMethodId);
}