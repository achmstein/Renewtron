namespace Renewtron.Data;

public class BulkRenewalUpload
{
    public Guid Id { get; set; }

    public string BusinessName { get; set; } = string.Empty;
    public string Abn { get; set; } = string.Empty;
    public string? OwnerName { get; set; }

    public DateTime? RenewalDueDate { get; set; }
    public int RenewalYears { get; set; }
    public decimal Amount { get; set; }

    public BulkRenewalStatus Status { get; set; } = BulkRenewalStatus.WaitingForRenewalWindow;
    public DateTime UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SourceFile { get; set; }

    public Guid? RenewalRequestId { get; set; }
    public RenewalRequest? RenewalRequest { get; set; }
}
