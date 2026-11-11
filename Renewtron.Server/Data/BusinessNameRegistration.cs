namespace Renewtron.Data;

/// <summary>
/// A customer's application to register a new business name, from the public /register page:
/// they pay us (Stripe), then the asic-portal job fills ASIC Connect's "Apply for a Business
/// Name" wizard with our agent login and pays ASIC's fee (card in the same session, or BPAY
/// through Airwallex). Everything ASIC tells us is kept here.
/// </summary>
public class BusinessNameRegistration
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public BusinessNameRegistrationStatus Status { get; set; } = BusinessNameRegistrationStatus.AwaitingPayment;

    /* What the customer asked for */
    public string BusinessName { get; set; } = "";
    public string Abn { get; set; } = "";
    /// <summary>1 or 3.</summary>
    public int Years { get; set; } = 1;
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public RegistrationAddress ServiceAddress { get; set; } = new();
    public RegistrationAddress PrincipalAddress { get; set; } = new();

    /// <summary>Our copy of the register already had this exact name when they applied.</summary>
    public bool NameTakenLocally { get; set; }

    /* Payment to us */
    public decimal Amount { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public DateTime? PaidAt { get; set; }

    /* ASIC */
    public int AttemptCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>ASIC's transaction reference (1-11AQR319), given once the ABN is accepted.</summary>
    public string? AsicTransactionReference { get; set; }
    public string? AbrEntityName { get; set; }
    public string? AbrEntityType { get; set; }
    /// <summary>Available, ManualDecision, NotAvailable or Unknown, from ASIC's name check.</summary>
    public string? NameVerdict { get; set; }
    public string? NameVerdictText { get; set; }
    /// <summary>"Card" or "Bpay": how ASIC's fee was (to be) paid.</summary>
    public string? AsicPayBy { get; set; }
    public decimal? AsicFee { get; set; }
    public DateTime? LodgedAt { get; set; }
    public string? BpayBillerCode { get; set; }
    public string? BpayReference { get; set; }
    public string? AirwallexTransferId { get; set; }
    public string? AirwallexTransferStatus { get; set; }
    public int BpayPaymentAttempts { get; set; }
    public string? BpayPaymentError { get; set; }
    public DateTime? AsicPaidAt { get; set; }
    /// <summary>The last myTransactions row we read for it: transaction and payment status.</summary>
    public string? AsicTransactionStatus { get; set; }
    public string? AsicPaymentStatus { get; set; }
    public DateTime? AsicCheckedAt { get; set; }

    public string? FailedAtStep { get; set; }
    public string? Error { get; set; }
    /// <summary>The words of the last ASIC page we saw (for a person to read when it went wrong).</summary>
    public string? PageText { get; set; }

    public bool IsTest { get; set; }
    public string? VisitorId { get; set; }
    public string? Source { get; set; }
}

/// <summary>An Australian street address, in ASIC's fields.</summary>
public class RegistrationAddress
{
    public string Unit { get; set; } = "";
    public string StreetNumber { get; set; } = "";
    public string StreetName { get; set; } = "";
    /// <summary>ASIC's street type code: St, Rd, Ave, Pl …</summary>
    public string StreetType { get; set; } = "";
    public string Suburb { get; set; } = "";
    public string State { get; set; } = "";
    public string Postcode { get; set; } = "";

    public override string ToString() =>
        $"{(Unit.Length > 0 ? Unit + "/" : "")}{StreetNumber} {StreetName} {StreetType}, {Suburb} {State} {Postcode}".Trim();
}

public enum BusinessNameRegistrationStatus
{
    /// <summary>Started on the page; the customer hasn't paid us.</summary>
    AwaitingPayment,
    /// <summary>Paid; waiting for the asic-portal job.</summary>
    Queued,
    Running,
    /// <summary>Lodged with ASIC; ASIC's fee is still to be paid (BPAY) or confirmed.</summary>
    AwaitingAsicPayment,
    /// <summary>Lodged and ASIC's fee paid (or no fee asked yet: referred for ASIC's manual decision).</summary>
    Lodged,
    /// <summary>ASIC refused before lodging (name taken, ABN refused …): refund the customer.</summary>
    Refused,
    /// <summary>Something we couldn't read or finish: a person should look.</summary>
    NeedsLook,
    Failed,
    Refunded,
}
