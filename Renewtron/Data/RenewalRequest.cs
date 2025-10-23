namespace Renewtron.Data;

public class RenewalRequest
{
    public Guid Id { get; set; }
    public Guid SearchResultId { get; set; }
    public Guid? CustomerCreditCardId { get; set; } // Customer's card (for display only - Stripe payments)
    public DateTime InitiatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }

    // Renewal details
    public int RenewalYears { get; set; }
    public string? Email { get; set; }

    // Payment
    public PaymentType PaymentType { get; set; }
    public decimal Amount { get; set; } // Amount paid by customer
    public string? ExternalPaymentReference { get; set; } // Reference for manual payments (e.g., check number, wire confirmation)

    // Stripe Payment Info (only for Stripe payments)
    public string? StripePaymentIntentId { get; set; }
    public string? StripePaymentStatus { get; set; }
    public DateTime? StripePaidAt { get; set; }

    // Status tracking
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TransactionReference { get; set; } // ASIC transaction reference
    public string? HostedTokenizationId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailedAtStep { get; set; }

    // Navigation properties
    public SearchResult SearchResult { get; set; }
    public SavedCreditCard? CustomerCreditCard { get; set; }
}
