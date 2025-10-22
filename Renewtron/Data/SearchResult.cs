namespace Renewtron.Data;

public class SearchResult
{
    public Guid Id { get; set; }
    public Guid SearchLogId { get; set; }

    // Business Name Details
    public string BusinessName { get; set; }
    public string? Status { get; set; }
    public string? RegistrationDate { get; set; }
    public string? RenewalDate { get; set; }
    public string? CancelledDate { get; set; }
    public string? CancellationUnderReview { get; set; }
    public string? AddressForServiceDocuments { get; set; }
    public string? PrincipalPlaceOfBusiness { get; set; }

    // Navigation properties
    public SearchLog SearchLog { get; set; }
    public List<Holder> Holders { get; set; } = [];
    public RenewalRequest? RenewalRequest { get; set; }
}