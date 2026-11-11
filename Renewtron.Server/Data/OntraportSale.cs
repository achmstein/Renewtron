namespace Renewtron.Data;

public class OntraportSale
{
    public Guid Id { get; set; }
    /// <summary>The client and business name this sale is for (set by the client linker).</summary>
    public Guid? ClientId { get; set; }
    public Guid? ClientBusinessNameId { get; set; }

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

    /// <summary>
    /// Settled outside Renewtron (renewed by hand at ASIC, refunded, not renewing): off every work
    /// queue. HandledNote says why when Renewtron marked it itself (the old renewed-elsewhere cleanup; now the order's Outcome).
    /// </summary>
    public DateTime? HandledAt { get; set; }
    public string? HandledNote { get; set; }

    /// <summary>
    /// The ASIC name search that finds the account number before a renewal can start. When ASIC
    /// can't be reached it is tried again on its own (<see cref="NextSearchAt"/>), up to
    /// <see cref="Services.SearchRetry.MaxAttempts"/> times, rather than failing for good.
    /// </summary>
    public int SearchAttempts { get; set; }
    public DateTime? NextSearchAt { get; set; }

    // Link to RenewalRequest when queued
    public Guid? RenewalRequestId { get; set; }
    public RenewalRequest? RenewalRequest { get; set; }
}
