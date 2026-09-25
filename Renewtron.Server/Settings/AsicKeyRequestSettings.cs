namespace Renewtron.Settings;

/// <summary>
/// Outbound half of the ASIC key loop. After an Ontraport sale whose contact has no ASIC key,
/// Renewtron queues a request: everything a person needs to fill in ASIC's online enquiry
/// form asking for the key to be emailed to the inbox <see cref="AsicKeyInboxSettings"/>
/// scans. The form is submitted by hand (ASIC's captcha refuses the server's address), the
/// reference number is recorded against the request, and the inbox scanner closes it off
/// when the key arrives.
/// </summary>
public class AsicKeyRequestSettings
{
    /// <summary>Master switch: whether the sales sync queues requests at all.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Queue a request automatically for every eligible sale the daily Ontraport sync brings in.</summary>
    public bool AutoRequestOnSync { get; set; } = true;

    /// <summary>
    /// Where the enquiry text asks ASIC to email the key ({Email} in the template). Must be
    /// the inbox the scanner reads. The form's own contact email is the client's address,
    /// which comes with each sale, so ASIC sees the client as the person asking.
    /// </summary>
    public string RequestEmail { get; set; } = "businessnamerenewals@gmail.com";

    /// <summary>Phone to put on the form when the sale has no usable mobile number. ASIC requires one.</summary>
    public string DefaultPhonePrefix { get; set; } = "";
    public string DefaultPhoneNumber { get; set; } = "";

    /// <summary>Free-text enquiry. Placeholders: {FirstName} {LastName} {Abn} {BusinessName} {Email} {ClientEmail}.</summary>
    public string MessageTemplate { get; set; } = DefaultMessageTemplate;

    public const string DefaultMessageTemplate =
        "My name is {FirstName} {LastName} ABN {Abn} for my business name {BusinessName} please email a copy of my ASIC key to {Email}";
}
