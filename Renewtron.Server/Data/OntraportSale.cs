namespace Renewtron.Data;

public class OntraportSale
{
    public Guid Id { get; set; }

    // Ontraport identifiers
    public string OntraportContactId { get; set; } = string.Empty;
    public string OntraportTransactionId { get; set; } = string.Empty;

    // Contact info
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string? DateOfBirth { get; set; }

    // Business name details
    public string BusinessName { get; set; } = string.Empty;
    public string Abn { get; set; } = string.Empty;
    public string? BusinessNameOwner { get; set; }

    // Profile details for the Business Portal (refreshed on every sync)
    public string? Address { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }

    /// <summary>TFN, encrypted with IEncryptionService — never stored in plain text.</summary>
    public string? TfnEncrypted { get; set; }

    // Renewal details
    public DateTime? RenewalDueDate { get; set; }
    public int RenewalYears { get; set; }
    public decimal AmountPaid { get; set; }

    // Status tracking
    public OntraportSaleStatus Status { get; set; } = OntraportSaleStatus.Synced;
    public DateTime SyncedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }

    // Link to RenewalRequest when queued
    public Guid? RenewalRequestId { get; set; }
    public RenewalRequest? RenewalRequest { get; set; }
}
