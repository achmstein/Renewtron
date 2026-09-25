namespace Renewtron.Settings;

/// <summary>
/// Outbound half of the ASIC key loop: after an Ontraport sale, Renewtron fills in ASIC's
/// online enquiry form asking for the business name's ASIC key to be emailed to the inbox
/// that <see cref="AsicKeyInboxSettings"/> scans. The form sits behind reCAPTCHA v3 with a
/// server-side minimum score of 0.5. Tokens bought from a solving service score 0.1, so the
/// form is driven in a real (headless) Chromium shipped in the server image, whose own token
/// passes — verified from a home connection; the datacenter IP is what production tests.
/// </summary>
public class AsicKeyRequestSettings
{
    /// <summary>Master switch. Off by default so nothing is sent to ASIC until it's configured.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Create a request automatically for every eligible sale the daily Ontraport sync brings in.</summary>
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

    /// <summary>Cap per run so a backlog can't fire hundreds of enquiries at once (each is a browser session of a minute or so).</summary>
    public int MaxPerRun { get; set; } = 25;

    /// <summary>Page reloads for a fresh token when ASIC rejects the browser's captcha, per submission.</summary>
    public int MaxCaptchaAttempts { get; set; } = 3;

    /// <summary>Failed requests are retried on later runs up to this many attempts in total, when the failure looked transient.</summary>
    public int MaxAutoAttempts { get; set; } = 5;

    /// <summary>
    /// Seconds to wait between two submissions in one run. Eleven back to back from one address
    /// took ASIC's captcha score from 0.3 to 0.1 within a single run.
    /// </summary>
    public int PauseBetweenRequestsSeconds { get; set; } = 120;

    /// <summary>End the run after this many consecutive captcha rejections; the rest wait for the next run.</summary>
    public int StopRunAfterCaptchaFailures { get; set; } = 2;

    public const string DefaultMessageTemplate =
        "My name is {FirstName} {LastName} ABN {Abn} for my business name {BusinessName} please email a copy of my ASIC key to {Email}";
}
