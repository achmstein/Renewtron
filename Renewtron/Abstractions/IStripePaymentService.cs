namespace Renewtron.Abstractions;

public interface IStripePaymentService
{
    Task<(bool Success, string? PaymentIntentId, string? ErrorMessage)> ConfirmPaymentAsync(
        decimal amount,
        string customerEmail,
        string description,
        Dictionary<string, string> metadata,
        string paymentMethodId);
}
