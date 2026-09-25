using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asic.KeyTool;

/// <summary>
/// Everything the tool needs to fill in ASIC's enquiry form. Defaults here match what
/// Renewtron used to send, so a request typed on this machine reads the same as one the
/// server sent: same key delivery inbox, same wording.
/// </summary>
public sealed class KeyToolSettings
{
    /// <summary>
    /// Ontraport API credentials — the tool reads new paid renewals straight from Ontraport,
    /// the same contacts Renewtron's sales sync picks up.
    /// </summary>
    public string OntraportApiAppId { get; set; } = "";
    public string OntraportApiKey { get; set; } = "";

    /// <summary>How many of the most recent paid contacts to pull per fetch.</summary>
    public int OntraportFetchLimit { get; set; } = 200;

    /// <summary>
    /// Hold back sales paying less than this (the $39/$49 cancellation fee is below the
    /// one-year renewal price). 0 turns the guard off.
    /// </summary>
    public decimal MinimumAmountPaid { get; set; }

    /// <summary>
    /// How the form is submitted. "Browser" (default) drives a visible Chrome/Edge on this
    /// machine, whose own reCAPTCHA token passes ASIC's 0.5 minimum; "2Captcha" posts the
    /// form directly with a bought token, which has been scoring 0.1 since Sept 2026.
    /// </summary>
    public string SubmitVia { get; set; } = SubmitViaBrowser;
    public const string SubmitViaBrowser = "Browser";
    public const string SubmitVia2Captcha = "2Captcha";

    /// <summary>"chrome", "msedge", or blank to try Chrome then Edge.</summary>
    public string BrowserChannel { get; set; } = "";

    /// <summary>2Captcha API key. Only used when SubmitVia is "2Captcha"; each submission buys at least one token.</summary>
    public string TwoCaptchaApiKey { get; set; } = "";

    /// <summary>
    /// Score to ask 2Captcha for. ASIC requires ≥ 0.5; 2Captcha's default (0.3) tokens were
    /// rejected in testing and 0.9 tokens accepted. Higher scores cost more per solve.
    /// </summary>
    public double MinCaptchaScore { get; set; } = 0.9;

    /// <summary>
    /// Fresh tokens to try per submission before giving up. In browser mode that's page
    /// reloads; in 2Captcha mode each one is billed.
    /// </summary>
    public int MaxCaptchaAttempts { get; set; } = 3;

    /// <summary>
    /// Where the enquiry text asks ASIC to email the key ({Email} in the template). Must be
    /// the inbox Renewtron's scanner reads (AsicKeyInbox:Username on the server), or the key
    /// never reaches Ontraport. This is not the form's reply-to address — that's the
    /// client's own email, which comes with each sale from Ontraport.
    /// </summary>
    public string RequestEmail { get; set; } = "businessnamerenewals@gmail.com";

    /// <summary>Phone to put on the form when the sale has no usable mobile number.</summary>
    public string DefaultPhonePrefix { get; set; } = "";
    public string DefaultPhoneNumber { get; set; } = "";

    /// <summary>Free-text enquiry. Placeholders: {FirstName} {LastName} {Abn} {BusinessName} {Email}.</summary>
    public string MessageTemplate { get; set; } = DefaultMessageTemplate;

    /// <summary>
    /// Optional proxy, e.g. http://user:pass@host:port. Only needed when this machine's own
    /// connection is scored as a datacenter: ASIC passes the submitting IP to Google, and a
    /// datacenter address scores 0.1 where a residential one passes. Run the tool from a
    /// residential connection and leave this blank.
    /// </summary>
    public string ProxyUrl { get; set; } = "";

    public const string DefaultMessageTemplate =
        "My name is {FirstName} {LastName} ABN {Abn} for my business name {BusinessName} please email a copy of my ASIC key to {Email}";

    // ---- storage ---------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// %APPDATA%\Renewtron\asic-keytool.json — outside the install folder so an update (or a
    /// published single file) can't clobber the API key, and so it never lands in git.
    /// </summary>
    public static string UserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Renewtron", "asic-keytool.json");

    private static string ShippedSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>
    /// Defaults ← appsettings.json beside the exe ← the user file ← environment. The env
    /// vars (ASIC_KEYTOOL_2CAPTCHA_KEY, ASIC_KEYTOOL_PROXY_URL) are for running the tool
    /// without leaving the secrets on disk.
    /// </summary>
    public static KeyToolSettings Load()
    {
        var settings = ReadFile(ShippedSettingsPath) ?? new KeyToolSettings();
        if (ReadFile(UserSettingsPath) is { } user)
            settings = user;

        var key = Environment.GetEnvironmentVariable("ASIC_KEYTOOL_2CAPTCHA_KEY");
        if (!string.IsNullOrWhiteSpace(key)) settings.TwoCaptchaApiKey = key.Trim();
        var proxy = Environment.GetEnvironmentVariable("ASIC_KEYTOOL_PROXY_URL");
        if (!string.IsNullOrWhiteSpace(proxy)) settings.ProxyUrl = proxy.Trim();

        var appId = Environment.GetEnvironmentVariable("ASIC_KEYTOOL_ONTRAPORT_APPID");
        if (!string.IsNullOrWhiteSpace(appId)) settings.OntraportApiAppId = appId.Trim();
        var ontraportKey = Environment.GetEnvironmentVariable("ASIC_KEYTOOL_ONTRAPORT_KEY");
        if (!string.IsNullOrWhiteSpace(ontraportKey)) settings.OntraportApiKey = ontraportKey.Trim();

        if (string.IsNullOrWhiteSpace(settings.MessageTemplate)) settings.MessageTemplate = DefaultMessageTemplate;
        settings.SubmitVia = settings.UsesBrowser ? SubmitViaBrowser : SubmitVia2Captcha;
        settings.MaxCaptchaAttempts = Math.Clamp(settings.MaxCaptchaAttempts, 1, 5);
        settings.OntraportFetchLimit = Math.Clamp(settings.OntraportFetchLimit <= 0 ? 200 : settings.OntraportFetchLimit, 1, 1000);
        return settings;
    }

    public void Save()
    {
        var path = UserSettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static KeyToolSettings? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<KeyToolSettings>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool UsesBrowser => !string.Equals(SubmitVia?.Trim(), SubmitVia2Captcha, StringComparison.OrdinalIgnoreCase);

    /// <summary>What's missing before a submission can go out, for the UI to nag about.</summary>
    public string? Problem()
    {
        if (!UsesBrowser && string.IsNullOrWhiteSpace(TwoCaptchaApiKey)) return "No 2Captcha API key — Settings → 2Captcha API key (or switch Submit via to Browser).";
        if (string.IsNullOrWhiteSpace(RequestEmail)) return "No key delivery email — the enquiry has to say where ASIC should send the key.";
        return null;
    }
}
