namespace Renewtron.Data;

public class SearchResult
{
    public Guid Id { get; set; }
    public Guid SearchLogId { get; set; }

    // Business Name Details (from ASIC Renewal Service)
    public string BusinessName { get; set; }
    public string AccountNumber { get; set; }
    public string RegistrationDate { get; set; }

    // Navigation properties
    public SearchLog SearchLog { get; set; }
    public RenewalRequest? RenewalRequest { get; set; }
}