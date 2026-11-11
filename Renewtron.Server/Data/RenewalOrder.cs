namespace Renewtron.Data;

/// <summary>
/// One paid renewal: a client paid us to renew one business name for one term. It carries the
/// one status every page reads (<see cref="Stage"/> and <see cref="Reason"/>). The ASIC runs for it
/// are its attempts (<see cref="RenewalRequest"/>s with <c>RenewalOrderId</c>).
///
/// For now (phases 1–2 of the refactor) the order is a projection: <c>RenewalOrderProjection</c>
/// computes it from the sale/renewal records, and <c>RenewalOrderService</c> keeps it up to
/// date. Later the order itself is what the pipeline writes.
/// </summary>
public class RenewalOrder
{
    public Guid Id { get; set; }

    public RenewalOrderSource Source { get; set; }
    /// <summary>The record that paid for it: the Ontraport sale, or (wizard and manual) the renewal.</summary>
    public Guid SourceId { get; set; }

    public Guid? ClientId { get; set; }
    public Guid? ClientBusinessNameId { get; set; }
    public string Abn { get; set; } = "";
    public string BusinessName { get; set; } = "";
    /// <summary><see cref="Services.BusinessNameText.MatchKey"/> of the name: matches across spelling and spacing.</summary>
    public string NameKey { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public int Years { get; set; }
    public decimal AmountPaid { get; set; }
    public DateTime PaidAt { get; set; }

    public RenewalOrderStage Stage { get; set; }
    public RenewalOrderReason? Reason { get; set; }
    /// <summary>When something happens next on its own (a scheduled retry, the renewal window opening).</summary>
    public DateTime? NextActionAt { get; set; }
    public DateTime StageChangedAt { get; set; }
    public string? Note { get; set; }

    public Guid? CurrentAttemptId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? RenewedAt { get; set; }

    /// <summary>
    /// How the order ended, or why it stopped, when a person or Renewtron's ASIC check decided it
    /// (renewed elsewhere, lapsed, refunded…). Stored, unlike <see cref="Stage"/>: the projection
    /// reads it and only a renewal we actually lodged overrides it. Null while the pipeline decides.
    /// </summary>
    public RenewalOrderReason? Outcome { get; set; }
    public DateTime? OutcomeAt { get; set; }
    /// <summary>Who decided: the admin's email, or "ASIC check".</summary>
    public string? OutcomeBy { get; set; }
    /// <summary>What it rests on, in words (what ASIC showed, and when).</summary>
    public string? Evidence { get; set; }
    public OrderEvidenceSource? EvidenceSource { get; set; }
    /// <summary>When the evidence was seen (the register file's date, or when ASIC was asked).</summary>
    public DateTime? EvidenceAt { get; set; }
    /// <summary>A refund, dispute or ASIC reference that goes with the outcome.</summary>
    public string? OutcomeReference { get; set; }
    /// <summary>When Renewtron last asked ASIC's renewal page about this name.</summary>
    public DateTime? AsicCheckedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Where an order's outcome evidence came from.</summary>
public enum OrderEvidenceSource
{
    /// <summary>ASIC's public register file (data.gov.au), imported nightly.</summary>
    AsicRegister = 0,
    /// <summary>ASIC's renewal page: what it offers for renewal under the ABN.</summary>
    AsicRenewalPage = 1,
    /// <summary>A person, with their note.</summary>
    Staff = 2,
    Ontraport = 3,
}

public enum RenewalOrderSource
{
    Ontraport = 0,
    Wizard = 1,
    // 2 was Bulk (bulk uploads, removed Nov 2026).
    Manual = 3,
}

/// <summary>Where a paid renewal is. Exactly one at a time.</summary>
public enum RenewalOrderStage
{
    /// <summary>Paid early; ASIC's renewal window (29 days before due) hasn't opened.</summary>
    Waiting = 0,
    /// <summary>In the window (or overdue) and not started yet: the next processing run starts it.</summary>
    Due = 1,
    /// <summary>An ASIC run is under way, or a retry is scheduled.</summary>
    Renewing = 2,
    /// <summary>ASIC lodged it (paid by card, or a BPAY bill to pay).</summary>
    Renewed = 3,
    /// <summary>Stopped: a person has to act. <see cref="RenewalOrder.Reason"/> says why.</summary>
    NeedsPerson = 4,
    /// <summary>Finished without us renewing it (renewed elsewhere, refunded, cancelled…). Reason says why.</summary>
    Closed = 5,
}

public enum RenewalOrderReason
{
    // NeedsPerson
    /// <summary>The run reached ASIC's payment step and we can't tell whether ASIC took the money.</summary>
    VerifyPaymentAtAsic = 0,
    /// <summary>ASIC doesn't list the name under the ABN (cancelled, or we hold the wrong name).</summary>
    NameNotFound = 1,
    /// <summary>ASIC refused the details.</summary>
    AsicRefused = 2,
    /// <summary>Renewtron couldn't get through to ASIC and has stopped retrying.</summary>
    AttemptsExhausted = 3,
    /// <summary>
    /// Too late to renew online: more than <see cref="Services.RenewalOrderProjection.TooLateDays"/> days
    /// past the due date, when ASIC no longer offers it (or ASIC cancelled it). Register again or refund.
    /// </summary>
    TooLate = 4,
    /// <summary>Paid less than the renewal fee.</summary>
    BelowMinimumFee = 5,
    /// <summary>ASIC has another renewal open for the ABN.</summary>
    InProgressAtAsic = 6,
    /// <summary>ASIC declined our card and nothing retried it.</summary>
    CardDeclined = 7,
    /// <summary>Not renewable for another reason given by Ontraport.</summary>
    Ineligible = 8,
    /// <summary>ASIC cancelled the name (gone from the register after its due date). Refund, or offer to register it again.</summary>
    Lapsed = 9,
    /// <summary>ASIC lists names under the ABN but not this spelling: fix the name, then try again.</summary>
    NameMismatch = 10,

    // Waiting
    /// <summary>Still registered, but ASIC isn't offering it for renewal now. Checked again on its own.</summary>
    NotOfferedNow = 11,
    /// <summary>Neither Ontraport nor ASIC gave a due date, so Renewtron can't tell when to renew it.</summary>
    NoDueDate = 12,

    // Closed
    /// <summary>Renewed outside Renewtron (by hand at ASIC, or by the client).</summary>
    RenewedElsewhere = 20,
    Refunded = 21,
    ClientCancelling = 22,
    Disputed = 23,
    Duplicate = 24,
    /// <summary>Lapsed at ASIC; the client is offered a new registration instead, and the payment kept for it.</summary>
    RegisterAgain = 25,
}
