namespace Renewtron.Data;

/// <summary>
/// Outbox row for a renewal-outcome write-back to Ontraport (status field + due-date
/// advance + completed tag). The write used to be fire-and-forget — a failed sync meant
/// the contact's due date never moved and the next daily run could re-process them.
/// Rows are created alongside the renewal outcome and retried by a recurring job until
/// sent (SentAt set) or exhausted (AttemptCount cap).
/// </summary>
public class OntraportSyncOutbox
{
    public Guid Id { get; set; }
    public string OntraportContactId { get; set; } = "";
    public OntraportRenewalOutcome Outcome { get; set; }
    public DateTime? NewRenewalDueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
}
