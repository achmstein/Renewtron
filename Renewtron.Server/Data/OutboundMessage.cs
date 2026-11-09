namespace Renewtron.Data;

public enum OutboundChannel
{
    Email = 0,
    Sms = 1,
}

public enum OutboundStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>
/// One email (or SMS) Renewtron sent, or tried to: every send goes through <see cref="Services.EmailSender"/>,
/// which writes this row first and then marks it Sent (with SendGrid's message id) or Failed (with
/// SendGrid's answer). SendGrid's event webhook later adds what happened at the inbox (delivered,
/// bounced…). The admin's Emails page reads these rows.
/// </summary>
public class OutboundMessage
{
    public Guid Id { get; set; }
    /// <summary>The lead it's about, when there is one (most emails aren't about a lead).</summary>
    public Guid? LeadId { get; set; }
    public Lead? Lead { get; set; }
    /// <summary>The client it went to (set by the client linker).</summary>
    public Guid? ClientId { get; set; }

    public OutboundChannel Channel { get; set; }
    public string Template { get; set; } = string.Empty; // e.g. "win-back"
    /// <summary>The editable template it was written from (see <see cref="EmailTemplate"/>), when it was.</summary>
    public string? TemplateKey { get; set; }
    /// <summary>That template's version: 0 is the wording in code, otherwise an <see cref="EmailTemplateVersion"/>.</summary>
    public int? TemplateVersion { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty; // email or phone

    public OutboundStatus Status { get; set; } = OutboundStatus.Queued;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Optional batch ID so admins can group "send" runs together.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>The renewal it's about, when there is one.</summary>
    public Guid? RenewalRequestId { get; set; }
    /// <summary>The message it resends, for a Resend from the admin.</summary>
    public Guid? ResendOfId { get; set; }

    /// <summary>What was sent, so the admin can read it and resend it. Cleared after 180 days.</summary>
    public string? BodyHtml { get; set; }
    public string? BodyText { get; set; }

    /// <summary>SendGrid's X-Message-Id: its events carry it (as the start of sg_message_id).</summary>
    public string? ProviderMessageId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// What SendGrid last said happened at the inbox: delivered, deferred, bounce, dropped,
    /// spamreport. Null until its event webhook reports.
    /// </summary>
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveryAt { get; set; }
    /// <summary>The bounce or drop reason, when SendGrid gives one.</summary>
    public string? DeliveryDetail { get; set; }
    public DateTime? OpenedAt { get; set; }
}
