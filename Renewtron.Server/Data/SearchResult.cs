namespace Renewtron.Data;

public class SearchResult
{
    public Guid Id { get; set; }
    public Guid SearchLogId { get; set; }

    // Business Name Details (from ASIC Renewal Service)
    public string BusinessName { get; set; }
    public string AccountNumber { get; set; }
    public string RegistrationDate { get; set; }

    /// <summary>
    /// False when the name came from our local copy but ASIC's renewal search didn't list
    /// it (not due, already in progress, or no longer registered) — it can't be paid for.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    // Navigation properties
    public SearchLog SearchLog { get; set; }
    public RenewalRequest? RenewalRequest { get; set; }
}