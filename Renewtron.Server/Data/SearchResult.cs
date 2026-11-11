namespace Renewtron.Data;

public class SearchResult
{
    public Guid Id { get; set; }
    public Guid SearchLogId { get; set; }

    // Business Name Details (from ASIC Renewal Service)
    public string BusinessName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string RegistrationDate { get; set; } = string.Empty;

    /// <summary>
    /// False when the name came from our local copy but ASIC's renewal search didn't list
    /// it (not due, already in progress, or no longer registered) — it can't be paid for.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// ASIC isn't taking this name's renewal yet (it opens 29 days before the due date), but
    /// the customer may pay now: the renewal waits and Renewtron lodges it once ASIC lists the
    /// name (see <see cref="Services.EarlyRenewalService"/>). Its account number comes then.
    /// </summary>
    public bool PayEarly { get; set; }

    // Navigation properties
    public SearchLog SearchLog { get; set; } = null!;
    public RenewalRequest? RenewalRequest { get; set; }
}