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
    /// <summary>The ABNs this person is a contact for; the business names hang off the ABN.</summary>
    public List<ClientAbn> Abns { get; set; } = [];
}

/// <summary>Where we first saw a record.</summary>
public enum RecordSource
{
    Ontraport,
    Wizard,
    Renewal,
    Portal,
    KeyRequest,
    BulkUpload,
    Register,
    Staff,
}

/// <summary>Where a business name's due date came from, weakest first: a stronger source replaces a weaker one.</summary>
public enum RenewalDueDateSource
{
    /// <summary>Worked out by Renewtron's ASIC check (renewed elsewhere: the old due date plus a year) when ASIC's register search couldn't be read. The weakest.</summary>
    Estimated = 0,
    Ontraport = 1,
    BulkUpload = 2,
    /// <summary>Worked out from a renewal we lodged: the most reliable.</summary>
    Renewal = 3,
    /// <summary>ASIC's own renewal date for the name, read from its register search (AsicDueDateService).</summary>
    AsicRegister = 4,
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
    public RecordSource Source { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// An ABN, the holder ASIC registers business names to. Names belong here rather than to a
/// person, because that is how the register works: whoever deals with us about the ABN, the
/// names are the ABN's. People are attached through <see cref="ClientAbn"/>.
/// </summary>
public class AbnHolder
{
    /// <summary>11 digits; the key.</summary>
    public string Abn { get; set; } = "";
    /// <summary>When the public register was last read for names under this ABN.</summary>
    public DateTime? RegisterCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<ClientAbn> Contacts { get; set; } = [];
    public List<ClientBusinessName> BusinessNames { get; set; } = [];
}

/// <summary>
/// A person who is a contact for an ABN. A sole trader is the only contact on theirs; a
/// company can have a director and a bookkeeper. Exactly one contact per ABN gets the
/// renewal reminders and pays (<see cref="IsReminderContact"/>).
/// </summary>
public class ClientAbn
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    /// <summary>11 digits.</summary>
    public string Abn { get; set; } = "";
    public AbnHolder? Holder { get; set; }
    /// <summary>The one contact on the ABN who gets reminders about its names.</summary>
    public bool IsReminderContact { get; set; }
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
/// One business name, under the ABN that holds it (unique on ABN + name). Its people are the
/// ABN's contacts; the reminder contact is the one who hears about it.
/// </summary>
public class ClientBusinessName
{
    public Guid Id { get; set; }

    public string Abn { get; set; } = "";
    public AbnHolder? Holder { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Renewals fall on its anniversary.</summary>
    public DateOnly? RegistrationDate { get; set; }
    public string? AsicAccountNumber { get; set; }

    /// <summary>
    /// Next renewal due date, when we know it. The register file no longer publishes one (ASIC
    /// removed BN_RENEW_DT in April 2025), but ASIC's register search still shows each name's
    /// renewal date: the nightly due-date refresh reads it there (AsicDueDateService). Otherwise
    /// it comes from our own renewals, Ontraport's due-date field, or an estimate.
    /// </summary>
    public DateOnly? RenewalDueDate { get; set; }
    public RenewalDueDateSource? DueDateSource { get; set; }
    /// <summary>When ASIC's register search was last asked for this name's renewal date.</summary>
    public DateTime? DueDateCheckedAt { get; set; }

    public string? AsicKey { get; set; }
    public DateTime? AsicKeyReceivedAt { get; set; }
    public Guid? AsicKeyNotificationId { get; set; }
    /// <summary>When the name was linked to our ASIC Connect account (see <see cref="AsicPortalLink"/>).</summary>
    public DateTime? PortalLinkedAt { get; set; }

    public DateTime? LastRenewedAt { get; set; }
    public int? LastRenewalYears { get; set; }
    public Guid? LastRenewalRequestId { get; set; }

    public ClientBusinessNameStatus Status { get; set; } = ClientBusinessNameStatus.Active;
    public RecordSource Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
