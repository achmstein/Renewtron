namespace Renewtron.Data;

/// <summary>
/// Represents Stripe payment information for a renewal request
/// </summary>
public class StripePayment
{
    public Guid Id { get; set; }
    public Guid RenewalRequestId { get; set; }
    public Guid CustomerCreditCardId { get; set; } // Customer's card (for display only)

    public string PaymentIntentId { get; set; }
    public string PaymentStatus { get; set; } // succeeded, failed, etc.
    public DateTime? PaidAt { get; set; }

    // Navigation properties
    public RenewalRequest RenewalRequest { get; set; }
    public SavedCreditCard CustomerCreditCard { get; set; }
}
