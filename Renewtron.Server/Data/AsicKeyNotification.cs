namespace Renewtron.Data;

/// <summary>
/// One ASIC "Notification request" email and what we did with it. The row is the audit
/// trail the admin sees: which business, which key, which contact, and where it stopped
/// when it didn't get all the way through.
/// </summary>
public class AsicKeyNotification
{
    public Guid Id { get; set; }

    /// <summary>RFC 5322 Message-ID — the de-duplication key across scans.</summary>
    public string MessageId { get; set; } = "";
    public string? Subject { get; set; }
    public string? From { get; set; }
    public DateTime ReceivedAt { get; set; }

    /// <summary>"…a copy of a notification for {BusinessName}." from the email body.</summary>
    public string? BusinessName { get; set; }
    /// <summary>ABN when the PDF carries one — used as a second matching key.</summary>
    public string? Abn { get; set; }
    public string? DownloadUrl { get; set; }
    public string? AsicKey { get; set; }

    /// <summary>Comma-separated Ontraport contact IDs the key was written to.</summary>
    public string? OntraportContactIds { get; set; }
    public int OntraportContactsUpdated { get; set; }

    public AsicKeyNotificationStatus Status { get; set; } = AsicKeyNotificationStatus.Pending;
    public string? ErrorMessage { get; set; }
    /// <summary>Head of the PDF text, kept so a failed key match can be diagnosed from the admin.</summary>
    public string? PdfTextExcerpt { get; set; }

    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
