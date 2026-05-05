namespace Renewtron.Data;

/// <summary>
/// Represents a lead captured during the renewal flow
/// </summary>
public class Lead
{
    public Guid Id { get; set; }

    // ABN Information
    public string Abn { get; set; } = string.Empty;

    // Contact Information
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string? Tfn { get; set; }

    // Tracking
    public DateTime CreatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }

    // Outcome
    public LeadOutcome Outcome { get; set; } = LeadOutcome.Pending;
    public string? OutcomeMessage { get; set; }

    // Reminder opt-in
    public bool ReminderOptIn { get; set; }

    // Conversion tracking
    public bool ConvertedToRenewal { get; set; }
    public DateTime? ConvertedAt { get; set; }

    // Link to search when ASIC lookup is performed
    public Guid? SearchLogId { get; set; }
    public SearchLog? SearchLog { get; set; }

    // Renewal requests created from this lead
    public List<RenewalRequest> RenewalRequests { get; set; } = [];
}
