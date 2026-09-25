using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Asic.KeyTool;

/// <summary>
/// Drives ASIC's enquiry form in a real, visible Chrome (or Edge) instead of buying captcha
/// tokens. ASIC scores its reCAPTCHA v3 server-side and needs 0.5; tokens bought from a
/// solving farm score 0.1 whatever machine submits them, while the token a real browser
/// mints on a home connection passes. So the browser does the whole thing: load the page,
/// let it generate its token, pick the enquiry type, fill in the details, read the receipt.
///
/// The browser gets its own profile under %APPDATA%\Renewtron so it never touches the
/// operator's everyday Chrome, and so cookies and reputation build up between runs.
/// </summary>
public sealed class BrowserAsicKeyRequestClient
{
    private const string LandingUrl = AsicKeyRequestClient.BaseUrl + AsicKeyRequestClient.LandingPath;

    private const string Prefix = "message-1-formData-1-inquiryDetails-1-";
    private const string FieldType1 = Prefix + "inquiry-1-inquiryType1-1";
    private const string FieldType2 = Prefix + "inquiry-1-inquiryType2-1";
    private const string FieldQuestion = Prefix + "inquiry-1-question-1";
    private const string FieldEntityNumber = Prefix + "entityDetails-1-entityNumber-1";
    private const string FieldEntityName = Prefix + "entityDetails-1-entityName-1";
    private const string FieldGivenNames = Prefix + "contactDetails-1-person-1-personName-1-givenNames-1";
    private const string FieldFamilyName = Prefix + "contactDetails-1-person-1-personName-1-familyName-1";
    private const string FieldPhonePrefix = Prefix + "contactDetails-1-phoneNo-1-telephoneNumber-1-prefix-1";
    private const string FieldPhoneNumber = Prefix + "contactDetails-1-phoneNo-1-telephoneNumber-1-number-1";
    private const string FieldEmail = Prefix + "contactDetails-1-emailAddress-1";
    private const string FieldConfirmEmail = Prefix + "contactDetails-1-confirmEmailAddress-1";
    private const string FieldNeedAttachments = Prefix + "needAttachments-1";
    private const string FieldDeclaresAuthorised = "message-1-formData-1-declares-1-declaresAuthorised-1-isTrue-1";
    private const string FieldDeclaresPrivacy = "message-1-formData-1-declares-1-declaresPrivacy-1-isTrue-1";

    private const string Type1Value = "Business Name";
    private const string Type2Value = "Maintain information";

    private const string SubmitButton = "form[name='inquiryv001'] input[type='image']";
    private const string TokenReady = "() => { const t = document.getElementById('g-recaptcha-response'); return !!t && t.value.length > 0; }";

    private static readonly Regex ReferenceRegex = new(@"Reference\s+Number:?\s*(\d{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Which browsers to try, in order, when no channel is configured.</summary>
    public static readonly string[] DefaultChannels = ["chrome", "msedge"];

    public static string ProfileDirectory => Path.Combine(DataDirectory, "asic-keytool-browser");

    /// <summary>ASIC_KEYTOOL_DATA_DIR (a mounted volume in a container) or %APPDATA%\Renewtron.</summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("ASIC_KEYTOOL_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Renewtron");

    /// <summary>
    /// Headless, bundled-Chromium mode for a container (DOTNET_RUNNING_IN_CONTAINER=true or
    /// ASIC_KEYTOOL_HEADLESS=true). PATH_TO_CHROMIUM points at a system Chromium instead of
    /// Playwright's download. This is the experiment: whether a datacenter IP plus a headless
    /// browser still clears ASIC's 0.5, which bought tokens never did.
    /// </summary>
    public static bool Headless =>
        Environment.GetEnvironmentVariable("ASIC_KEYTOOL_HEADLESS") == "true" ||
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    private static string? ChromiumPath =>
        Environment.GetEnvironmentVariable("PATH_TO_CHROMIUM") is { Length: > 0 } p ? p : null;

    private const string FallbackUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    /// <summary>Where the last unexpected page is written so a failure can be looked at.</summary>
    public static string LastPagePath => Path.Combine(DataDirectory, "asic-keytool-last-page.html");

    private readonly string? _channel;
    private readonly Action<string>? _progress;

    /// <param name="channel">"chrome", "msedge", or null to try both in that order.</param>
    public BrowserAsicKeyRequestClient(string? channel, Action<string>? progress = null)
    {
        _channel = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim().ToLowerInvariant();
        _progress = progress;
    }

    public static string Describe(string channel) => channel switch
    {
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "chromium" => "headless Chromium",
        _ => channel,
    };

    /// <summary>
    /// Opens the landing page and waits for it to mint a token — proves the browser launches,
    /// the page loads and reCAPTCHA isn't blocked, without submitting anything.
    /// </summary>
    public async Task<(string Browser, bool TokenObtained, string? Error)> ProbeAsync(CancellationToken ct = default)
    {
        await using var session = await LaunchAsync(ct);
        if (session.Error != null) return ("", false, session.Error);
        try
        {
            var page = session.Page!;
            await page.GotoAsync(LandingUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
            await page.WaitForFunctionAsync(TokenReady, null, new() { Timeout = 90_000 });
            return (Describe(session.Channel), true, null);
        }
        catch (TimeoutException)
        {
            var diag = await DiagnoseAsync(session.Page!, session.ConsoleErrors);
            return (Describe(session.Channel), false, $"the page loaded but reCAPTCHA never produced a token (is google.com reachable from this connection?). {diag}");
        }
        catch (PlaywrightException ex)
        {
            return (Describe(session.Channel), false, Tidy(ex.Message));
        }
    }

    /// <summary>
    /// One enquiry, start to finish. <paramref name="maxAttempts"/> caps how many times the
    /// landing page is reloaded for a fresh token after ASIC rejects one; each attempt is a
    /// page load, not a purchase.
    /// </summary>
    public async Task<AsicKeyRequestResult> SubmitAsync(AsicKeyRequestInput input, int maxAttempts, CancellationToken ct = default)
    {
        var attempts = 0;
        await using var session = await LaunchAsync(ct);
        if (session.Error != null)
            return AsicKeyRequestResult.Failed(session.Error, 0, transient: false);

        var page = session.Page!;
        try
        {
            // ---- Page 1: enquiry type + the browser's own captcha token ----------------
            string? lastCaptchaError = null;
            var onDetails = false;
            for (attempts = 1; attempts <= Math.Max(1, maxAttempts); attempts++)
            {
                ct.ThrowIfCancellationRequested();
                Report(attempts == 1 ? $"opening ASIC's form in {Describe(session.Channel)}" : $"reloading for a fresh token (attempt {attempts})");
                await page.GotoAsync(LandingUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });

                Report("waiting for the page's reCAPTCHA token");
                try
                {
                    await page.WaitForFunctionAsync(TokenReady, null, new() { Timeout = 90_000 });
                }
                catch (TimeoutException)
                {
                    return AsicKeyRequestResult.Failed(
                        "ASIC's page never produced a reCAPTCHA token. Check that google.com is reachable from this connection.",
                        attempts, transient: true);
                }

                await page.SelectOptionAsync(Name(FieldType1), Type1Value);
                await page.SelectOptionAsync(Name(FieldType2), Type2Value);

                Report("submitting the enquiry type");
                var before = page.Url;
                await page.ClickAsync(SubmitButton);
                await page.WaitForURLAsync(u => u != before, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });

                if (await page.Locator(Name(FieldEntityNumber)).CountAsync() > 0)
                {
                    onDetails = true;
                    break;
                }

                var errors = await VisibleErrorsAsync(page);
                var captchaError = errors.FirstOrDefault(e => e.Contains("CAPTCHA", StringComparison.OrdinalIgnoreCase));
                if (captchaError != null)
                {
                    // "CAPTCHA validation failed, Score :0.3; minimum score require : 0.5" — reload for a fresh token.
                    lastCaptchaError = captchaError;
                    continue;
                }

                await SaveLastPageAsync(page);
                var summary = errors.Count > 0 ? string.Join("; ", errors) : "unexpected page after the enquiry type step";
                return AsicKeyRequestResult.Failed($"ASIC rejected the enquiry type page: {summary}", attempts, transient: errors.Count == 0);
            }

            if (!onDetails)
            {
                attempts = Math.Max(1, maxAttempts);
                return AsicKeyRequestResult.Failed(
                    $"ASIC did not accept the browser's captcha after {attempts} attempt(s). ASIC said: {lastCaptchaError ?? "no token accepted"}",
                    attempts, transient: true);
            }

            // ---- Page 2: enquiry details ---------------------------------------------
            Report("filling in the enquiry details");
            await page.FillAsync(Name(FieldQuestion), input.Question ?? "");
            await page.FillAsync(Name(FieldEntityNumber), DigitsOnly(input.Abn));
            await page.FillAsync(Name(FieldEntityName), Truncate(input.BusinessName, 200));
            await page.FillAsync(Name(FieldGivenNames), Truncate(input.GivenNames, 140));
            await page.FillAsync(Name(FieldFamilyName), Truncate(input.FamilyName, 40));
            await page.FillAsync(Name(FieldPhonePrefix), Truncate(input.PhonePrefix, 4));
            await page.FillAsync(Name(FieldPhoneNumber), Truncate(input.PhoneNumber, 15));
            await page.FillAsync(Name(FieldEmail), Truncate(input.Email, 50));
            await page.FillAsync(Name(FieldConfirmEmail), Truncate(input.Email, 50));

            await CheckIfPresentAsync(page, $"input[name='{FieldNeedAttachments}'][value='false']");
            await CheckIfPresentAsync(page, Name(FieldDeclaresAuthorised));
            await CheckIfPresentAsync(page, Name(FieldDeclaresPrivacy));

            Report("submitting the enquiry");
            var detailsUrl = page.Url;
            await page.ClickAsync(SubmitButton);
            try
            {
                await page.WaitForURLAsync(u => u != detailsUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
            }
            catch (TimeoutException)
            {
                // Client-side validation keeps the page where it is; fall through and read its errors.
            }

            var text = await page.InnerTextAsync("body");
            var reference = ReferenceRegex.Match(text);
            if (reference.Success)
                return AsicKeyRequestResult.Succeeded(reference.Groups[1].Value, attempts);

            await SaveLastPageAsync(page);
            var detailErrors = await VisibleErrorsAsync(page);
            var message = detailErrors.Count > 0
                ? string.Join("; ", detailErrors)
                : text.Contains("Thank", StringComparison.OrdinalIgnoreCase)
                    ? "ASIC accepted the enquiry but no reference number was found on the receipt."
                    : "unexpected page after the details step";
            return AsicKeyRequestResult.Failed($"ASIC rejected the enquiry details: {message}", attempts, transient: detailErrors.Count == 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException ex)
        {
            await SaveLastPageAsync(page);
            return AsicKeyRequestResult.Failed($"ASIC's page did not respond in time: {Tidy(ex.Message)}", attempts, transient: true);
        }
        catch (PlaywrightException ex)
        {
            await SaveLastPageAsync(page);
            return AsicKeyRequestResult.Failed($"Browser error: {Tidy(ex.Message)}", attempts, transient: true);
        }
        catch (Exception ex)
        {
            return AsicKeyRequestResult.Failed($"Unexpected error: {ex.Message}", attempts, transient: false);
        }
    }

    // ---- browser session ------------------------------------------------------------

    private sealed class Session : IAsyncDisposable
    {
        public IPlaywright? Playwright;
        public IBrowserContext? Context;
        public IPage? Page;
        public string Channel = "";
        public string? Error;
        public readonly List<string> ConsoleErrors = [];

        public async ValueTask DisposeAsync()
        {
            try { if (Context != null) await Context.CloseAsync(); } catch { /* closing */ }
            Playwright?.Dispose();
        }
    }

    /// <summary>
    /// A visible window with the tool's own profile. Automation flags are left off so the
    /// page sees an ordinary browser — with them on, Google scores the visit as a bot.
    /// </summary>
    private async Task<Session> LaunchAsync(CancellationToken ct)
    {
        var session = new Session();
        try
        {
            session.Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or InvalidOperationException)
        {
            session.Error = $"Could not start the browser driver: {Tidy(ex.Message)}";
            return session;
        }

        Directory.CreateDirectory(ProfileDirectory);
        var headless = Headless;
        // Headless/container: Playwright's own Chromium (or PATH_TO_CHROMIUM), no channel.
        var channels = headless ? ["chromium"] : _channel != null ? [_channel] : DefaultChannels;
        var failures = new List<string>();
        foreach (var channel in channels)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var args = new List<string> { "--disable-blink-features=AutomationControlled", "--window-size=1100,900", "--no-first-run", "--no-default-browser-check" };
                if (headless) args.AddRange(["--no-sandbox", "--disable-dev-shm-usage"]);

                // Bundled Chromium announces itself as "HeadlessChrome"; Google scores that as a
                // bot before it looks at anything else. Read the real UA and clean it.
                string? userAgent = null;
                if (headless) userAgent = await ResolveCleanUserAgentAsync(session.Playwright, args);

                session.Context = await session.Playwright.Chromium.LaunchPersistentContextAsync(ProfileDirectory, new()
                {
                    Channel = headless ? null : channel,
                    ExecutablePath = headless ? ChromiumPath : null,
                    Headless = headless,
                    UserAgent = userAgent,
                    IgnoreDefaultArgs = ["--enable-automation"],
                    Args = args,
                    ViewportSize = headless ? new ViewportSize { Width = 1100, Height = 900 } : ViewportSize.NoViewport,
                    Locale = "en-AU",
                    TimezoneId = "Australia/Sydney",
                });
                session.Channel = channel;
                session.Page = session.Context.Pages.Count > 0 ? session.Context.Pages[0] : await session.Context.NewPageAsync();
                session.Page.SetDefaultTimeout(30_000);
                session.Page.Console += (_, msg) => { if (msg.Type == "error" && session.ConsoleErrors.Count < 10) session.ConsoleErrors.Add(msg.Text); };
                session.Page.PageError += (_, err) => { if (session.ConsoleErrors.Count < 10) session.ConsoleErrors.Add(err); };
                return session;
            }
            catch (PlaywrightException ex)
            {
                failures.Add($"{Describe(channel)}: {Tidy(ex.Message)}");
            }
        }

        session.Error = channels.Length == 1
            ? $"Could not start {Describe(channels[0])} — {failures[0]}"
            : "No browser could be started. Install Google Chrome (or Microsoft Edge). " + string.Join(" · ", failures);
        return session;
    }

    // ---- helpers --------------------------------------------------------------------

    private static async Task<string> ResolveCleanUserAgentAsync(IPlaywright playwright, List<string> args)
    {
        try
        {
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = ChromiumPath, Args = args });
            var page = await browser.NewPageAsync();
            var ua = await page.EvaluateAsync<string>("() => navigator.userAgent");
            return string.IsNullOrWhiteSpace(ua) ? FallbackUserAgent : ua.Replace("HeadlessChrome", "Chrome");
        }
        catch (PlaywrightException)
        {
            return FallbackUserAgent;
        }
    }

    private void Report(string message) => _progress?.Invoke(message);

    private static string Name(string field) => $"[name='{field}']";

    private static async Task CheckIfPresentAsync(IPage page, string selector)
    {
        var locator = page.Locator(selector);
        if (await locator.CountAsync() > 0) await locator.First.CheckAsync();
    }

    private static async Task<List<string>> VisibleErrorsAsync(IPage page)
    {
        var texts = await page.Locator(".error:visible").AllInnerTextsAsync();
        return texts
            .Select(t => Regex.Replace(t, @"\s+", " ").Trim())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
    }

    /// <summary>ASIC_KEYTOOL_DIAG_DIR overrides where the last page and a screenshot go.</summary>
    private static string DiagDirectory =>
        Environment.GetEnvironmentVariable("ASIC_KEYTOOL_DIAG_DIR") is { Length: > 0 } dir ? dir : Path.GetDirectoryName(LastPagePath)!;

    private static async Task SaveLastPageAsync(IPage page)
    {
        try
        {
            Directory.CreateDirectory(DiagDirectory);
            await File.WriteAllTextAsync(Path.Combine(DiagDirectory, Path.GetFileName(LastPagePath)), await page.ContentAsync());
            await page.ScreenshotAsync(new() { Path = Path.Combine(DiagDirectory, "asic-keytool-last-page.png"), FullPage = true });
        }
        catch { /* diagnostics only */ }
    }

    /// <summary>What the page sees, for the error message when the token never arrives.</summary>
    private static async Task<string> DiagnoseAsync(IPage page, List<string> consoleErrors)
    {
        await SaveLastPageAsync(page);
        try
        {
            var state = await page.EvaluateAsync<string>(
                "() => JSON.stringify({ url: location.href, title: document.title, grecaptcha: typeof grecaptcha, " +
                "field: !!document.getElementById('g-recaptcha-response'), " +
                "scripts: [...document.scripts].map(s => s.src).filter(s => s.includes('recaptcha')) })");
            var errors = consoleErrors.Count > 0 ? " Console: " + string.Join(" | ", consoleErrors) : "";
            return $"Page: {state}.{errors} Saved to {DiagDirectory}.";
        }
        catch (PlaywrightException ex)
        {
            return $"Could not inspect the page: {Tidy(ex.Message)}";
        }
    }

    /// <summary>Playwright's messages carry a call log after the first line; keep the first line.</summary>
    private static string Tidy(string message)
    {
        var line = message.Split('\n')[0].Trim();
        if (line.Contains("is not found", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            return "not installed";
        return line;
    }

    private static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static string Truncate(string? value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..max];
    }
}
