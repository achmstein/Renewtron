namespace Renewtron.Data;

/// <summary>
/// One ASIC key request for one business name: created when a sale lands (or by hand),
/// submitted to ASIC's online enquiry form in a browser, then closed off when the key
/// notification shows up in the inbox. The row is the audit trail the admin sees end to end.
/// </summary>
public class AsicKeyRequest
{
    public Guid Id { get; set; }

    /// <summary>The sale that triggered it, when there was one.</summary>
    public Guid? OntraportSaleId { get; set; }
    public OntraportSale? OntraportSale { get; set; }
    public string? OntraportContactId { get; set; }

    // What goes on the form — snapshotted from the sale so a later contact edit can't change
    // what we told ASIC.
    public string GivenNames { get; set; } = "";
    public string FamilyName { get; set; } = "";
    /// <summary>The client's email: the form's reply-to, so ASIC sees the client as the enquirer.</summary>
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Abn { get; set; } = "";
    public string BusinessName { get; set; } = "";
    /// <summary>The rendered enquiry text actually sent.</summary>
    public string? Question { get; set; }

    /// <summary>"Sync" (created by the daily sales sync) or "Manual" (admin button).</summary>
    public string Source { get; set; } = "Sync";

    public AsicKeyRequestStatus Status { get; set; } = AsicKeyRequestStatus.Pending;
    /// <summary>ASIC's reference number from the Thank You page.</summary>
    public string? AsicReferenceNumber { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Captcha attempts (page loads) across all submissions.</summary>
    public int CaptchaSolves { get; set; }
    /// <summary>
    /// After a failure: true when the cause looked transient (low captcha score, ASIC outage)
    /// so the next run may retry; false for validation errors that need a human first.
    /// </summary>
    public bool CanAutoRetry { get; set; } = true;

    /// <summary>The inbox notification that carried the key, once matched.</summary>
    public Guid? AsicKeyNotificationId { get; set; }
    public AsicKeyNotification? AsicKeyNotification { get; set; }

    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Last submission attempt (success or failure).</summary>
    public DateTime? ProcessedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? KeyReceivedAt { get; set; }
}
