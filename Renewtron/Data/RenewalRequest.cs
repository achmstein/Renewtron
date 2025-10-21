namespace Renewtron.Data;

public class RenewalRequest
{
    public Guid Id { get; set; }
    public Guid SearchResultId { get; set; }
    public Guid? CustomerCreditCardId { get; set; } // Customer's card (for display only)
    public DateTime InitiatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }

    // Renewal details
    public int RenewalYears { get; set; }
    public string? Email { get; set; }

    // Pricing
    public decimal CustomerAmount { get; set; } // Amount charged to customer (ASIC + markup)
    public decimal AsicAmount { get; set; } // Amount paid to ASIC

    // Stripe Payment Info
    public string? StripePaymentIntentId { get; set; }
    public string? StripePaymentStatus { get; set; }
    public DateTime? StripePaidAt { get; set; }

    // Status tracking
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TransactionReference { get; set; }
    public string? HostedTokenizationId { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation properties
    public SearchResult SearchResult { get; set; }
    public SavedCreditCard? CustomerCreditCard { get; set; }
}