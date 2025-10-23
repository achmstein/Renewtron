namespace Renewtron.Data;

/// <summary>
/// Represents Stripe payment information for a renewal request
/// </summary>
public class StripePayment
{
    public Guid Id { get; set; }
    public Guid RenewalRequestId { get; set; }

    public string PaymentIntentId { get; set; }
    public string PaymentStatus { get; set; } // succeeded, failed, etc.
    public DateTime? PaidAt { get; set; }

    // Navigation property
    public RenewalRequest RenewalRequest { get; set; }
}
