using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asic.KeyTool;

/// <summary>
/// Everything the tool needs to fill in ASIC's enquiry form. Defaults here match what
/// Renewtron used to send, so a request sent from this machine reads the same as one the
/// server sent: same key delivery inbox, same wording.
/// </summary>
public sealed class KeyToolSettings
{
    /// <summary>
    /// Renewtron's admin login. The server keeps the list of ASIC key requests (one per paid
    /// sale whose contact has no key, from its own Ontraport sync); this tool fetches that
    /// list, sends each one through the browser and records the reference back.
    /// </summary>
    public string ServerUrl { get; set; } = "https://businessnames.applyforanabn.au";
    public string ServerEmail { get; set; } = "";
    public string ServerPassword { get; set; } = "";

    /// <summary>"chrome", "msedge", or blank to try Chrome then Edge.</summary>
    public string BrowserChannel { get; set; } = "";

    /// <summary>
    /// Page reloads to try per enquiry when ASIC scores the browser's token too low, half a
    /// minute apart, before giving up on that one.
    /// </summary>
    public int MaxTokenAttempts { get; set; } = 3;

    /// <summary>
    /// Seconds to wait between two enquiries in one run. Back-to-back submissions from one
    /// address drag Google's score down (eleven in a row took it from 0.3 to 0.1), so a
    /// batch is paced. 0 sends them one after another.
    /// </summary>
    public int PauseBetweenRequestsSeconds { get; set; } = 120;

    /// <summary>
    /// Where the enquiry text asks ASIC to email the key ({Email} in the template). Must be
    /// the inbox Renewtron's scanner reads (AsicKeyInbox:Username on the server), or the key
    /// never reaches Ontraport. This is not the form's reply-to address — that's the
    /// client's own email, which comes with each row from Renewtron.
    /// </summary>
    public string RequestEmail { get; set; } = "businessnamerenewals@gmail.com";

    /// <summary>Phone to put on the form when the row has no usable mobile number.</summary>
    public string DefaultPhonePrefix { get; set; } = "";
    public string DefaultPhoneNumber { get; set; } = "";

    /// <summary>Free-text enquiry. Placeholders: {FirstName} {LastName} {Abn} {BusinessName} {Email}.</summary>
    public string MessageTemplate { get; set; } = DefaultMessageTemplate;

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
    /// published single file) can't clobber the login, and so it never lands in git.
    /// </summary>
    public static string UserSettingsPath => Path.Combine(BrowserAsicKeyRequestClient.DataDirectory, "asic-keytool.json");

    private static string ShippedSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>
    /// appsettings.Development.json beside the exe: the developer's own copy with the login
    /// filled in. It is gitignored and never published, and it wins over the saved user file
    /// so a dev run doesn't depend on what was last saved from the menu.
    /// </summary>
    public static string DevelopmentSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");

    public static bool DevelopmentSettingsInUse => File.Exists(DevelopmentSettingsPath);

    /// <summary>
    /// Defaults ← appsettings.json beside the exe ← the user file ← appsettings.Development.json
    /// ← environment. The env vars are for running the tool without leaving the login on disk.
    /// </summary>
    public static KeyToolSettings Load()
    {
        var settings = ReadFile(ShippedSettingsPath) ?? new KeyToolSettings();
        if (ReadFile(UserSettingsPath) is { } user)
            settings = user;
        if (ReadFile(DevelopmentSettingsPath) is { } development)
            settings = development;

        var serverEmail = Environment.GetEnvironmentVariable("ASIC_KEYTOOL_SERVER_EMAIL");
        if (!string.IsNullOrWhiteSpace(serverEmail)) settings.ServerEmail = serverEmail.Trim();
        var serverPassword = Environment.GetEnvironmentVariable("ASIC_KEYTOOL_SERVER_PASSWORD");
        if (!string.IsNullOrWhiteSpace(serverPassword)) settings.ServerPassword = serverPassword;

        if (string.IsNullOrWhiteSpace(settings.MessageTemplate)) settings.MessageTemplate = DefaultMessageTemplate;
        settings.MaxTokenAttempts = Math.Clamp(settings.MaxTokenAttempts <= 0 ? 3 : settings.MaxTokenAttempts, 1, 5);
        settings.PauseBetweenRequestsSeconds = Math.Clamp(settings.PauseBetweenRequestsSeconds, 0, 3600);
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

    /// <summary>What's missing before a submission can go out, for the UI to nag about.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(RequestEmail)) return "No key delivery email — the enquiry has to say where ASIC should send the key.";
        return null;
    }
}
