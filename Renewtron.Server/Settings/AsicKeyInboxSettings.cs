namespace Renewtron.Settings;

/// <summary>
/// The Gmail inbox that receives ASIC's "Notification request" emails. Each one links to a
/// PDF carrying the business name's ASIC key, which we write back onto the Ontraport contact.
/// </summary>
public class AsicKeyInboxSettings
{
    public bool Enabled { get; set; } = true;

    // Gmail IMAP with a Google "app password" (the account needs 2-step verification on).
    public string ImapHost { get; set; } = "imap.gmail.com";
    public int ImapPort { get; set; } = 993;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Folder { get; set; } = "INBOX";

    /// <summary>Only messages whose subject contains this are considered.</summary>
    public string SubjectFilter { get; set; } = "Notification request";

    /// <summary>How far back each scan looks; ASIC's download links expire after 30 days anyway.</summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>Ontraport custom field (e.g. "f5xxx") that receives the ASIC key. Empty = not configured.</summary>
    public string OntraportFieldId { get; set; } = "";

    /// <summary>
    /// Regex (case-insensitive) run over the PDF text; group 1 is the key, whitespace inside
    /// it is stripped. ASIC uses several letters — "Here is the ASIC Key for NAME: 1-…",
    /// "ASIC Key: 1-…" on renewal notices, "The ASIC key for this business name is 1-…" on
    /// renewal confirmations — and long names wrap the key across a line after the hyphen.
    /// The default anchors on the "1-" + digits shape after any "ASIC key" mention.
    /// </summary>
    public string AsicKeyPattern { get; set; } = DefaultAsicKeyPattern;

    public const string DefaultAsicKeyPattern = @"ASIC\s*key\b[\s\S]{0,200}?(\d{1,3}-\s*\d{6,14})\b";
}
