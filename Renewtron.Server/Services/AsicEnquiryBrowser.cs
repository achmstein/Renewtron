using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Renewtron.Services;

/// <summary>What goes into ASIC's online enquiry form when asking for a business name's ASIC key.</summary>
public sealed class AsicEnquiryInput
{
    public string GivenNames { get; init; } = "";
    public string FamilyName { get; init; } = "";
    /// <summary>Entity number on the form — the ABN, digits only.</summary>
    public string Abn { get; init; } = "";
    /// <summary>Entity name on the form — the registered business name.</summary>
    public string BusinessName { get; init; } = "";
    /// <summary>The form's contact email — the client's own address.</summary>
    public string Email { get; init; } = "";
    public string PhonePrefix { get; init; } = "";
    public string PhoneNumber { get; init; } = "";
    /// <summary>The free-text enquiry ("My name is … please email a copy of my ASIC key to …").</summary>
    public string Question { get; init; } = "";
}

public sealed class AsicEnquiryResult
{
    public bool Success { get; init; }
    /// <summary>ASIC's "Reference Number" from the Thank You page.</summary>
    public string? ReferenceNumber { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>Page loads spent on captcha tokens.</summary>
    public int Attempts { get; init; }
    /// <summary>True when a later attempt could plausibly succeed (low captcha score, network blip).</summary>
    public bool Transient { get; init; }

    public static AsicEnquiryResult Succeeded(string reference, int attempts) =>
        new() { Success = true, ReferenceNumber = reference, Attempts = attempts };

    public static AsicEnquiryResult Failed(string error, int attempts, bool transient) =>
        new() { Success = false, ErrorMessage = error, Attempts = attempts, Transient = transient };
}

/// <summary>
/// Drives ASIC's "Online Enquiry" form (edge.asic.gov.au) in a real Chromium instead of
/// buying captcha tokens. ASIC scores its reCAPTCHA v3 server-side and needs 0.5; tokens
/// from a solving farm score 0.1 whatever submits them, while the token a real browser mints
/// passes. So the browser does the whole thing: load the page, let it generate its token,
/// pick "Business Name" → "Maintain information", fill in the details, read the receipt.
///
/// In the container (DOTNET_RUNNING_IN_CONTAINER) it runs Playwright's bundled Chromium
/// headless with the sandbox flags a root/tiny-shm container needs and a cleaned user agent
/// (the bundled build says "HeadlessChrome", which Google scores as a bot on sight). In local
/// dev it drives the installed Edge, headed, so the run can be watched. The profile lives
/// under the data directory so cookies and reputation persist across restarts.
/// </summary>
public sealed class AsicEnquiryBrowser
{
    public const string BaseUrl = "https://www.edge.asic.gov.au/008/";
    public const string LandingUrl = BaseUrl + "inquiryV001?start/landingPage";

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
    private const string FallbackUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    private static readonly Regex ReferenceRegex = new(@"Reference\s+Number:?\s*(\d{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _profileDirectory;
    private readonly string _diagnosticsDirectory;
    private readonly ILogger<AsicEnquiryBrowser> _logger;

    public AsicEnquiryBrowser(IConfiguration configuration, IWebHostEnvironment environment, ILogger<AsicEnquiryBrowser> logger)
    {
        // Beside the settings overrides: /data in the container, the content root in dev.
        var dataDir = configuration["Storage:OverridesPath"] is { Length: > 0 } overrides
            ? Path.GetDirectoryName(overrides) ?? environment.ContentRootPath
            : environment.ContentRootPath;
        _profileDirectory = Path.Combine(dataDir, "asic-enquiry-browser");
        _diagnosticsDirectory = Path.Combine(dataDir, "asic-enquiry-diagnostics");
        _logger = logger;
    }

    private static bool Headless =>
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
        Environment.GetEnvironmentVariable("ASIC_ENQUIRY_HEADLESS") == "true";

    private static string? ChromiumPath =>
        Environment.GetEnvironmentVariable("PATH_TO_CHROMIUM") is { Length: > 0 } p ? p : null;

    public static string BrowserName => Headless ? "headless Chromium" : "Microsoft Edge";

    /// <summary>
    /// Opens the landing page and waits for it to mint a token — proves the browser launches,
    /// the page loads and reCAPTCHA isn't blocked, without submitting anything.
    /// </summary>
    public async Task<(bool Ok, string? Error)> ProbeAsync(CancellationToken ct = default)
    {
        await using var session = await LaunchAsync(ct);
        if (session.Error != null) return (false, session.Error);
        try
        {
            var page = session.Page!;
            await page.GotoAsync(LandingUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
            await page.WaitForFunctionAsync(TokenReady, null, new() { Timeout = 90_000 });
            return (true, null);
        }
        catch (TimeoutException)
        {
            await SaveDiagnosticsAsync(session.Page!);
            var errors = session.ConsoleErrors.Count > 0 ? " Console: " + string.Join(" | ", session.ConsoleErrors) : "";
            return (false, $"The page loaded but reCAPTCHA never produced a token (is google.com reachable from the container?).{errors}");
        }
        catch (PlaywrightException ex)
        {
            return (false, Tidy(ex.Message));
        }
    }

    /// <summary>
    /// One enquiry, start to finish. <paramref name="maxAttempts"/> caps how many times the
    /// landing page is reloaded for a fresh token after ASIC rejects one.
    /// </summary>
    public async Task<AsicEnquiryResult> SubmitAsync(AsicEnquiryInput input, int maxAttempts, CancellationToken ct = default)
    {
        var attempts = 0;
        await using var session = await LaunchAsync(ct);
        if (session.Error != null)
            return AsicEnquiryResult.Failed(session.Error, 0, transient: true);

        var page = session.Page!;
        try
        {
            // ---- Page 1: enquiry type + the browser's own captcha token ----------------
            string? lastCaptchaError = null;
            var onDetails = false;
            for (attempts = 1; attempts <= Math.Max(1, maxAttempts); attempts++)
            {
                ct.ThrowIfCancellationRequested();
                await page.GotoAsync(LandingUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
                try
                {
                    await page.WaitForFunctionAsync(TokenReady, null, new() { Timeout = 90_000 });
                }
                catch (TimeoutException)
                {
                    await SaveDiagnosticsAsync(page);
                    return AsicEnquiryResult.Failed(
                        "ASIC's page never produced a reCAPTCHA token. Check that google.com is reachable from the container.",
                        attempts, transient: true);
                }

                await page.SelectOptionAsync(Name(FieldType1), Type1Value);
                await page.SelectOptionAsync(Name(FieldType2), Type2Value);

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
                    _logger.LogWarning("ASIC rejected the browser's captcha (attempt {Attempt}): {Error}", attempts, captchaError);
                    lastCaptchaError = captchaError;
                    continue;
                }

                await SaveDiagnosticsAsync(page);
                var summary = errors.Count > 0 ? string.Join("; ", errors) : "unexpected page after the enquiry type step";
                return AsicEnquiryResult.Failed($"ASIC rejected the enquiry type page: {summary}", attempts, transient: errors.Count == 0);
            }

            if (!onDetails)
            {
                attempts = Math.Max(1, maxAttempts);
                return AsicEnquiryResult.Failed(
                    $"ASIC did not accept the browser's captcha after {attempts} attempt(s). ASIC said: {lastCaptchaError ?? "no token accepted"}",
                    attempts, transient: true);
            }

            // ---- Page 2: enquiry details ---------------------------------------------
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
                return AsicEnquiryResult.Succeeded(reference.Groups[1].Value, attempts);

            await SaveDiagnosticsAsync(page);
            var detailErrors = await VisibleErrorsAsync(page);
            var message = detailErrors.Count > 0
                ? string.Join("; ", detailErrors)
                : text.Contains("Thank", StringComparison.OrdinalIgnoreCase)
                    ? "ASIC accepted the enquiry but no reference number was found on the receipt."
                    : "unexpected page after the details step";
            return AsicEnquiryResult.Failed($"ASIC rejected the enquiry details: {message}", attempts, transient: detailErrors.Count == 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException ex)
        {
            await SaveDiagnosticsAsync(page);
            return AsicEnquiryResult.Failed($"ASIC's page did not respond in time: {Tidy(ex.Message)}", attempts, transient: true);
        }
        catch (PlaywrightException ex)
        {
            await SaveDiagnosticsAsync(page);
            return AsicEnquiryResult.Failed($"Browser error: {Tidy(ex.Message)}", attempts, transient: true);
        }
        catch (Exception ex)
        {
            return AsicEnquiryResult.Failed($"Unexpected error: {ex.Message}", attempts, transient: false);
        }
    }

    // ---- browser session ------------------------------------------------------------

    private sealed class Session : IAsyncDisposable
    {
        public IPlaywright? Playwright;
        public IBrowserContext? Context;
        public IPage? Page;
        public string? Error;
        public readonly List<string> ConsoleErrors = [];

        public async ValueTask DisposeAsync()
        {
            try { if (Context != null) await Context.CloseAsync(); } catch { /* closing */ }
            Playwright?.Dispose();
        }
    }

    private async Task<Session> LaunchAsync(CancellationToken ct)
    {
        var session = new Session();
        try
        {
            session.Playwright = await Playwright.CreateAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or InvalidOperationException)
        {
            session.Error = $"Could not start the browser driver: {Tidy(ex.Message)}";
            return session;
        }

        try
        {
            Directory.CreateDirectory(_profileDirectory);
            var headless = Headless;
            var args = new List<string> { "--disable-blink-features=AutomationControlled", "--window-size=1100,900", "--no-first-run", "--no-default-browser-check" };
            if (headless) args.AddRange(["--no-sandbox", "--disable-dev-shm-usage"]);

            string? userAgent = null;
            if (headless) userAgent = await ResolveCleanUserAgentAsync(session.Playwright, args);

            ct.ThrowIfCancellationRequested();
            session.Context = await session.Playwright.Chromium.LaunchPersistentContextAsync(_profileDirectory, new()
            {
                Channel = headless ? null : "msedge",
                ExecutablePath = headless ? ChromiumPath : null,
                Headless = headless,
                UserAgent = userAgent,
                IgnoreDefaultArgs = ["--enable-automation"],
                Args = args,
                ViewportSize = headless ? new ViewportSize { Width = 1100, Height = 900 } : ViewportSize.NoViewport,
                Locale = "en-AU",
                TimezoneId = "Australia/Sydney",
            });
            session.Page = session.Context.Pages.Count > 0 ? session.Context.Pages[0] : await session.Context.NewPageAsync();
            session.Page.SetDefaultTimeout(30_000);
            session.Page.Console += (_, msg) => { if (msg.Type == "error" && session.ConsoleErrors.Count < 10) session.ConsoleErrors.Add(msg.Text); };
            session.Page.PageError += (_, err) => { if (session.ConsoleErrors.Count < 10) session.ConsoleErrors.Add(err); };
            return session;
        }
        catch (PlaywrightException ex)
        {
            session.Error = $"Could not start {BrowserName}: {Tidy(ex.Message)}";
            return session;
        }
    }

    // Bundled Chromium announces itself as "HeadlessChrome"; read the real UA and clean it.
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

    // ---- helpers --------------------------------------------------------------------

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

    /// <summary>The last unexpected page and a screenshot, so a failure can be looked at.</summary>
    private async Task SaveDiagnosticsAsync(IPage page)
    {
        try
        {
            Directory.CreateDirectory(_diagnosticsDirectory);
            await File.WriteAllTextAsync(Path.Combine(_diagnosticsDirectory, "last-page.html"), await page.ContentAsync());
            await page.ScreenshotAsync(new() { Path = Path.Combine(_diagnosticsDirectory, "last-page.png"), FullPage = true });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not save ASIC enquiry diagnostics");
        }
    }

    /// <summary>Playwright's messages carry a call log after the first line; keep the first line.</summary>
    private static string Tidy(string message)
    {
        var line = message.Split('\n')[0].Trim();
        if (line.Contains("is not found", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            return "browser not installed";
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
