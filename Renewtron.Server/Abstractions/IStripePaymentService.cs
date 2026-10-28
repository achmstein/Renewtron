namespace Renewtron.Abstractions;

/// <summary>Card display info pulled off the PaymentIntent's charge (never the raw PAN).</summary>
public record StripeCardInfo(string? Brand, string? Last4, string? ExpMonth, string? ExpYear);

/// <summary>
/// Outcome of a server-side confirm. Exactly one of three shapes:
/// Success (charge done), RequiresAction (browser must run 3D Secure with ClientSecret,
/// then call the completion endpoint), or failure (ErrorMessage set).
/// </summary>
public record StripeConfirmResult(
    bool Success,
    bool RequiresAction,
    string? PaymentIntentId,
    string? ClientSecret,
    StripeCardInfo? Card,
    string? ErrorMessage);

/// <summary>Snapshot of an existing PaymentIntent, used to verify a 3DS completion.</summary>
public record StripeIntentState(
    string PaymentIntentId,
    string Status,
    long AmountInCents,
    IReadOnlyDictionary<string, string> Metadata,
    StripeCardInfo? Card);

public interface IStripePaymentService
{
    /// <param name="idempotencyKey">Identifies the order: the same key always returns the same
    /// PaymentIntent, and each attempt confirms it with that attempt's card.</param>
    Task<StripeConfirmResult> ConfirmPaymentAsync(
        decimal amount,
        string customerEmail,
        string description,
        Dictionary<string, string> metadata,
        string paymentMethodId,
        string? idempotencyKey = null,
        bool testMode = false);

    /// <param name="testMode">Use the test customers' Stripe test keys (Settings → Test mode).</param>
    Task<StripeIntentState?> GetPaymentIntentAsync(string paymentIntentId, bool testMode = false);
}
