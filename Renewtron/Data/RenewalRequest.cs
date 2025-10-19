namespace Renewtron.Data;

public class RenewalRequest
{
    public Guid Id { get; set; }
    public Guid SearchResultId { get; set; }
    public Guid? SavedCreditCardId { get; set; }
    public DateTime InitiatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }

    // Renewal details
    public int RenewalYears { get; set; }
    public string? Email { get; set; }

    // Status tracking
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TransactionReference { get; set; }
    public string? HostedTokenizationId { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation properties
    public SearchResult SearchResult { get; set; }
    public SavedCreditCard? SavedCreditCard { get; set; }
}