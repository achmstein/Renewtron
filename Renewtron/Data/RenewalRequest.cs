namespace Renewtron.Data;

public class RenewalRequest
{
    public Guid Id { get; set; }
    public Guid SearchResultId { get; set; }
    public DateTime InitiatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }

    // Renewal details
    public int RenewalYears { get; set; }
    public string? Email { get; set; }

    // Payment
    public PaymentType PaymentType { get; set; }
    public decimal Amount { get; set; } // Amount paid by customer

    // Status tracking
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TransactionReference { get; set; } // ASIC transaction reference
    public string? HostedTokenizationId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailedAtStep { get; set; }

    // Navigation properties
    public SearchResult SearchResult { get; set; }
    public StripePayment? StripePayment { get; set; } // Only populated for Stripe payments
}
