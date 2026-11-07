namespace Renewtron.Settings;

/// <summary>
/// Our ASIC Connect agent login, used to link a client's business name to our account (with the
/// client's ASIC key and date of birth) and then take over its contact details: the business
/// email, the holder's email and (last) the representatives. Started from the admin, one name at a time.
/// </summary>
public class AsicPortalSettings
{
    /// <summary>Master switch for the "Link to our ASIC account" button.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>ASIC Connect username (an email address) and password.</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>The email put on the business name and on its holder once it's linked.</summary>
    public string BusinessEmail { get; set; } = "businessnames@applyforanabn.com.au";

    /// <summary>
    /// Representatives to keep, comma separated. Every other representative is removed. A
    /// representative is kept when its row contains any of these (case does not matter).
    /// </summary>
    /// "sdfgsdfg David sdfgsdfg" is our own ASIC Connect account's person, added as a
    /// representative when we link a name: removing it takes away our access to the name
    /// (7 Nov 2026, NAKED SKIN AESTHETICA lost its change actions that way).
    public string KeepRepresentatives { get; set; } = "David Orth, Business David Names Individual, Business Name Renewals Individual, Business Names, sdfgsdfg David sdfgsdfg";

    /// <summary>
    /// How we declare on ASIC's Declare page unless a run says otherwise: OnBehalfOfHolder
    /// ("submitting on behalf of the holder and authorised to") or Holder ("I am the holder").
    /// The admin button lets you pick per run.
    /// </summary>
    public Asic.Client.AsicDeclareAs DeclareAs { get; set; } = Asic.Client.AsicDeclareAs.Holder;

    /// <summary>Ontraport field set to "success" or "fail" after a link ("Did ASIC Key Link?").</summary>
    public string OntraportLinkFieldId { get; set; } = "f5479";

    /// <summary>Ontraport campaign a contact is added to when ASIC rejects their key. Blank = don't.</summary>
    public string WrongKeyCampaignId { get; set; } = "";

    public IReadOnlyList<string> KeepNames() =>
        (KeepRepresentatives ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool HasLogin => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
