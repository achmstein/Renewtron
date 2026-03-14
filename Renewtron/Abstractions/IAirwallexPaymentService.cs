namespace Renewtron.Abstractions;

public interface IAirwallexPaymentService
{
    Task<(bool Success, string? PaymentIntentId, string? ClientSecret, string? ErrorMessage)> CreatePaymentIntentAsync(
        decimal amount,
        string merchantOrderId,
        Dictionary<string, string> metadata);

    Task<(bool Success, string? Status, string? ErrorMessage)> VerifyPaymentIntentAsync(string paymentIntentId);
}
