namespace Renewtron.Settings;

/// <summary>
/// Outbound half of the ASIC key loop: after an Ontraport sale, Renewtron fills in ASIC's
/// online enquiry form asking for the business name's ASIC key to be emailed to the inbox
/// that <see cref="AsicKeyInboxSettings"/> scans. The form sits behind reCAPTCHA v3 with a
/// server-side minimum score of 0.5, so tokens are bought from 2Captcha.
/// </summary>
public class AsicKeyRequestSettings
{
    /// <summary>Master switch. Off by default so nothing is sent to ASIC until it's configured.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Create a request automatically for every sale the daily Ontraport sync brings in.</summary>
    public bool AutoRequestOnSync { get; set; } = true;

    public string TwoCaptchaApiKey { get; set; } = "";

    /// <summary>
    /// Score to ask 2Captcha for. ASIC requires ≥ 0.5; 2Captcha's default (0.3) tokens were
    /// rejected in testing and 0.9 tokens accepted. Higher scores cost more per solve.
    /// </summary>
    public double MinCaptchaScore { get; set; } = 0.9;

    /// <summary>Fresh tokens to try per submission before giving up (each one is billed).</summary>
    public int MaxCaptchaAttempts { get; set; } = 3;

    /// <summary>Where ASIC should email the key. Must be the inbox the scanner reads.</summary>
    public string RequestEmail { get; set; } = "businessnames@idealbusiness.com.au";

    /// <summary>Phone to put on the form when the sale has no usable mobile number.</summary>
    public string DefaultPhonePrefix { get; set; } = "02";
    public string DefaultPhoneNumber { get; set; } = "";

    /// <summary>Free-text enquiry. Placeholders: {FirstName} {LastName} {Abn} {BusinessName} {Email}.</summary>
    public string MessageTemplate { get; set; } = DefaultMessageTemplate;

    /// <summary>Cap per run so a backlog can't fire hundreds of enquiries (and captcha solves) at once.</summary>
    public int MaxPerRun { get; set; } = 25;

    /// <summary>
    /// Optional proxy for the ASIC form traffic only, e.g. http://user:pass@host:port. ASIC passes
    /// the submitting IP to Google when it verifies the captcha token, and a datacenter address
    /// (the Lightsail host) scores 0.1 where a residential one passes — so route through one.
    /// </summary>
    public string ProxyUrl { get; set; } = "";

    public const string DefaultMessageTemplate =
        "My name is {FirstName} {LastName} ABN {Abn} for my business name {BusinessName} please email a copy of my ASIC key to {Email}";
}
