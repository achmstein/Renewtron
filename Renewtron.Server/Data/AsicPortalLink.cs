namespace Renewtron.Data;

/// <summary>
/// One run of "link to our ASIC account" for one business name: in ASIC Connect, link the name
/// with the client's ASIC key and date of birth, then change its business email to ours, change
/// the holder's email to ours, and last remove every representative but ours (removing them can
/// take away our access to the holder change, so it goes last). Each step's outcome is kept, so
/// a retry starts at the first step that isn't done.
/// </summary>
public class AsicPortalLink
{
    public Guid Id { get; set; }

    public Guid? ClientId { get; set; }
    public Guid ClientBusinessNameId { get; set; }

    // What we used, snapshotted when the run was asked for.
    public string BusinessName { get; set; } = "";
    public string Abn { get; set; } = "";
    public string AsicKey { get; set; } = "";
    /// <summary>dd/MM/yyyy. ASIC only checks that it's a date.</summary>
    public string DateOfBirth { get; set; } = "";
    /// <summary>Where the date of birth came from: Client, Sale or Today (none on file).</summary>
    public string DateOfBirthSource { get; set; } = "";
    public string? OntraportContactId { get; set; }

    /// <summary>
    /// How this run declares on ASIC's Declare pages (business email, holder, representatives):
    /// chosen when the run is started, defaulting to the setting.
    /// </summary>
    public Asic.Client.AsicDeclareAs DeclareAs { get; set; } = Asic.Client.AsicDeclareAs.Holder;

    public AsicPortalLinkStatus Status { get; set; } = AsicPortalLinkStatus.Queued;

    /// <summary>success, already_exists, failed or unknown.</summary>
    public string? LinkOutcome { get; set; }
    /// <summary>success, failed or unknown.</summary>
    public string? EmailOutcome { get; set; }
    /// <summary>ASIC's transaction reference for the business email change.</summary>
    public string? EmailReference { get; set; }
    /// <summary>no_action (none left to remove), success, failed or unknown.</summary>
    public string? RepsOutcome { get; set; }
    /// <summary>Representatives removed, one per line.</summary>
    public string? RepsRemoved { get; set; }
    /// <summary>ASIC's transaction references for those removals, one per line.</summary>
    public string? RepsReferences { get; set; }
    /// <summary>success, no_action (a company holder), no_address (none to keep), failed, unknown or pending.</summary>
    public string? HolderOutcome { get; set; }
    /// <summary>The holder's type as ASIC shows it ("Individual", "Body Corporate"…).</summary>
    public string? HolderType { get; set; }
    public string? HolderReference { get; set; }

    /// <summary>Where it stopped and why, for the record page.</summary>
    public string? FailedAtStep { get; set; }
    public string? Error { get; set; }
    /// <summary>File name of the page as it looked when a step went wrong (under the data folder).</summary>
    public string? Screenshot { get; set; }

    /// <summary>What Ontraport was told: "success" or "fail", or the reason it wasn't.</summary>
    public string? OntraportResult { get; set; }

    public string? RequestedBy { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public enum AsicPortalLinkStatus
{
    /// <summary>Waiting for the ASIC Connect worker.</summary>
    Queued,
    Running,
    /// <summary>Linked, and the email, holder and representatives are ours.</summary>
    Done,
    /// <summary>ASIC didn't accept the key and date of birth: the client needs to send the right key.</summary>
    WrongKey,
    /// <summary>Linked, but a later step failed or its result wasn't clear: look, then retry.</summary>
    NeedsLook,
    /// <summary>Couldn't get through (login, ASIC down, a page that didn't load).</summary>
    Failed,
}
