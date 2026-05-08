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
/// Records an outbound message (email/SMS) sent to a lead — used for win-back campaigns
/// and to give David an audit trail of who's been contacted and when.
/// </summary>
public class OutboundMessage
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }

    public OutboundChannel Channel { get; set; }
    public string Template { get; set; } = string.Empty; // e.g. "win-back"
    public string Subject { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty; // email or phone

    public OutboundStatus Status { get; set; } = OutboundStatus.Queued;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Optional batch ID so admins can group "send" runs together.</summary>
    public Guid? BatchId { get; set; }
}
