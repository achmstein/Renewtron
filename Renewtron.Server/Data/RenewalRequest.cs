namespace Renewtron.Data;

public class RenewalRequest
{
    public Guid Id { get; set; }
    public Guid SearchResultId { get; set; }
    public DateTime InitiatedAt { get; set; }

    // Link to Lead (for new flow)
    public Guid? LeadId { get; set; }

    // Renewal details
    public int RenewalYears { get; set; }
    public string? Email { get; set; }
    public string? MobileNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Tfn { get; set; }

    // Source tracking
    public RenewalSource Source { get; set; } = RenewalSource.Renewtron;

    /// <summary>From a test customer: paid with Stripe's test keys and never sent to ASIC.</summary>
    public bool IsTest { get; set; }

    // Payment
    public PaymentType PaymentType { get; set; }
    public decimal Amount { get; set; } // Amount paid by customer

    // Status tracking
    public RenewalStatus Status { get; set; } = RenewalStatus.Pending;
    public DateTime? CompletedAt { get; set; }
    public string? TransactionReference { get; set; } // ASIC transaction reference
    public string? HostedTokenizationId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailedAtStep { get; set; }

    // Lodged with ASIC's "Pay later -> BPAY": ASIC hasn't been paid until someone pays these
    // at the bank (ASIC shows them only once). BpayPaidAt is set when staff mark it paid.
    public string? BpayBillerCode { get; set; }
    public string? BpayReference { get; set; }
    public DateTime? BpayPaidAt { get; set; }

    // Card attempts at ASIC that failed without taking money (declined, or the payment
    // didn't go through). Once it reaches AsicSettings.BpayAfterCardFailures, runs pay by BPAY.
    public int CardFailureCount { get; set; }

    // Retry tracking. AttemptCount counts processing runs; ErrorCategory is one of
    // RenewalErrorCategories; NextRetryAt is set when an automatic retry is scheduled.
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? ErrorCategory { get; set; }

    // Navigation properties
    public SearchResult SearchResult { get; set; }
    public StripePayment? StripePayment { get; set; } // Only populated for Stripe payments
    public Lead? Lead { get; set; }
}
