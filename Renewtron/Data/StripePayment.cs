namespace Renewtron.Data;

/// <summary>
/// Represents Stripe payment information for a renewal request
/// </summary>
public class StripePayment
{
    public Guid Id { get; set; }
    public Guid RenewalRequestId { get; set; }

    public string PaymentIntentId { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public DateTime? PaidAt { get; set; }

    // Card info from Stripe (for display only)
    public string? CardholderName { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
    public string? CardExpMonth { get; set; }
    public string? CardExpYear { get; set; }

    // Navigation properties
    public RenewalRequest RenewalRequest { get; set; } = null!;
}
