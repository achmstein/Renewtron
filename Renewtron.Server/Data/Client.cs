namespace Renewtron.Data;

/// <summary>
/// One person we deal with, however they reached us (Ontraport sale, wizard, portal, staff).
/// Ties together their email addresses, ABNs and every business name they hold, so a client
/// with three names is one record with three names rather than three unrelated rows.
/// Built and kept up to date by <see cref="Services.ClientLinkService"/>; the older records
/// (sales, leads, renewals…) keep their own snapshot of the contact details and gain a link.
/// </summary>
public class Client
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = "";
    public string? MobileNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    /// <summary>TFN, encrypted with IEncryptionService, copied from the Ontraport sale.</summary>
    public string? TfnEncrypted { get; set; }

    /// <summary>Has paid us for something (a sale, a renewal): reminders about their other names are allowed.</summary>
    public bool HasPurchased { get; set; }
    /// <summary>Ticked the wizard's reminder box.</summary>
    public bool ReminderOptIn { get; set; }
    /// <summary>Asked us to stop emailing; no reminder goes out while this is set.</summary>
    public DateTime? UnsubscribedAt { get; set; }
    public bool IsTest { get; set; }

    /// <summary>
    /// Set when this client was created because a record matched another client's ABN but not
    /// their email or date of birth (a director and a bookkeeper, say). Staff merge or dismiss.
    /// </summary>
    public Guid? PossibleDuplicateOfId { get; set; }
    public Client? PossibleDuplicateOf { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Newest record linked to this client.</summary>
    public DateTime LastSeenAt { get; set; }

    public List<ClientEmail> Emails { get; set; } = [];
    public List<ClientAbn> Abns { get; set; } = [];
    public List<ClientBusinessName> BusinessNames { get; set; } = [];
}

/// <summary>An address the client has used. Ontraport keys contacts on email, so the contact id lives here.</summary>
public class ClientEmail
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    /// <summary>Stored lower-cased and trimmed: it is the matching key.</summary>
    public string Email { get; set; } = "";
    public string? OntraportContactId { get; set; }
    public bool IsPrimary { get; set; }
    /// <summary>Where we first saw it: Ontraport, Wizard, Renewal, Portal, KeyRequest, Staff.</summary>
    public string Source { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>An ABN the client holds. A client can hold several (sole trader plus a company, say).</summary>
public class ClientAbn
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    /// <summary>11 digits.</summary>
    public string Abn { get; set; } = "";
    /// <summary>When the public register was last read for names under this ABN.</summary>
    public DateTime? RegisterCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ClientBusinessNameStatus
{
    /// <summary>Registered and the client's.</summary>
    Active = 0,
    /// <summary>No longer on the public register.</summary>
    Cancelled = 1,
    /// <summary>Staff marked it as not this client's concern (left out of reminders).</summary>
    Excluded = 2,
}

/// <summary>
/// One business name a client holds. A name belongs to exactly one client (unique on ABN +
/// name), so two people sharing an ABN never both get its reminder or both pay for it.
/// </summary>
public class ClientBusinessName
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public string Abn { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>dd/MM/yyyy, as ASIC and the register give it. Renewals fall on its anniversary.</summary>
    public string? RegistrationDate { get; set; }
    public string? AsicAccountNumber { get; set; }

    /// <summary>
    /// Next renewal due date, when we know it. The public register no longer publishes one
    /// (ASIC removed BN_RENEW_DT in April 2025) and terms are 1 or 3 years, so it comes from
    /// our own renewals, Ontraport's due-date field or bulk uploads.
    /// </summary>
    public DateOnly? RenewalDueDate { get; set; }
    /// <summary>Renewal, Ontraport, BulkUpload.</summary>
    public string? DueDateSource { get; set; }

    public string? AsicKey { get; set; }
    public DateTime? AsicKeyReceivedAt { get; set; }
    public Guid? AsicKeyNotificationId { get; set; }

    public DateTime? LastRenewedAt { get; set; }
    public int? LastRenewalYears { get; set; }
    public Guid? LastRenewalRequestId { get; set; }

    public ClientBusinessNameStatus Status { get; set; } = ClientBusinessNameStatus.Active;
    /// <summary>Where we first saw it: Register, Ontraport, Wizard, Renewal, BulkUpload, KeyRequest.</summary>
    public string Source { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
