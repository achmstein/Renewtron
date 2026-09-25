using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Asic.KeyTool;

/// <summary>
/// Drives ASIC's enquiry form in a visible Google Chrome (or Microsoft Edge) on this machine,
/// in front of the operator. ASIC's reCAPTCHA v3 is invisible: the page mints its own token
/// when a real browser loads it and ASIC scores that token server-side (minimum 0.5). There is
/// nothing to solve — the browser does the whole thing: load the page, let it generate its
/// token, pick the enquiry type, fill in the details, read the reference off the receipt.
///
/// The browser runs with the tool's own profile under %APPDATA%\Renewtron. It can't borrow
/// the operator's everyday profile — Chromium (136 and later) refuses remote control of the
/// default profile — so the operator signs this profile into Google once (<see cref="SignInAsync"/>)
/// and its cookies and reputation build up between runs.
/// </summary>
public sealed class BrowserAsicKeyRequestClient
{
    public const string BaseUrl = "https://www.edge.asic.gov.au/008/";
    public const string LandingPath = "inquiryV001?start/landingPage";
    public const string LandingUrl = BaseUrl + LandingPath;
    private const string GoogleSignInUrl = "https://accounts.google.com/";

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

    /// <summary>Reloading straight after a refusal only adds to the burst Google is scoring.</summary>
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(30);

    private static readonly Regex ReferenceRegex = new(@"Reference\s+Number:?\s*(\d{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Which browsers to try, in order, when no channel is configured.</summary>
    public static readonly string[] DefaultChannels = ["chrome", "msedge"];

    public static string ProfileDirectory => Path.Combine(DataDirectory, "asic-keytool-browser");

    /// <summary>ASIC_KEYTOOL_DATA_DIR or %APPDATA%\Renewtron.</summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("ASIC_KEYTOOL_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Renewtron");

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
        _ => channel,
    };

    /// <summary>The public IP ASIC (and Google) will see for this machine's traffic.</summary>
    public static async Task<string> GetEgressIpAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync("https://api.ipify.org", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(ct)).Trim();
    }

    /// <summary>
    /// Opens the landing page and waits for it to mint a token — proves the browser launches,
    /// the page loads and reCAPTCHA isn't blocked, without submitting anything. Also says
    /// whether the profile is signed into Google, which is what lifts the score.
    /// </summary>
    public async Task<(string Browser, bool TokenObtained, bool SignedInToGoogle, string? Error)> ProbeAsync(CancellationToken ct = default)
    {
        await using var session = await LaunchAsync(ct);
        if (session.Error != null) return ("", false, false, session.Error);
        var signedIn = await SignedInToGoogleAsync(session.Context!);
        try
        {
            var page = session.Page!;
            await page.GotoAsync(LandingUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
            await page.WaitForFunctionAsync(TokenReady, null, new() { Timeout = 90_000 });
            return (Describe(session.Channel), true, signedIn, null);
        }
        catch (TimeoutException)
        {
            var diag = await DiagnoseAsync(session.Page!, session.ConsoleErrors);
            return (Describe(session.Channel), false, signedIn, $"the page loaded but reCAPTCHA never produced a token (is google.com reachable from this connection?). {diag}");
        }
        catch (PlaywrightException ex)
        {
            return (Describe(session.Channel), false, signedIn, Tidy(ex.Message));
        }
    }

    /// <summary>
    /// Opens the tool's browser on Google's sign-in page and waits for the operator to close
    /// the window. Whatever they signed into stays in the profile, so from then on Google sees
    /// a browser it knows when ASIC's page asks it for a token.
    /// </summary>
    public async Task<(string Browser, bool SignedInToGoogle, string? Error)> SignInAsync(CancellationToken ct = default)
    {
        await using var session = await LaunchAsync(ct);
        if (session.Error != null) return ("", false, session.Error);
        try
        {
            var closed = new TaskCompletionSource();
            session.Context!.Close += (_, _) => closed.TrySetResult();
            await session.Page!.GotoAsync(GoogleSignInUrl, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
            await closed.Task.WaitAsync(ct);
            return (Describe(session.Channel), true, null);
        }
        catch (PlaywrightException ex)
        {
            return (Describe(session.Channel), false, Tidy(ex.Message));
        }
    }

    /// <summary>
    /// One enquiry, start to finish. <paramref name="maxAttempts"/> caps how many times the
    /// landing page is reloaded for a fresh token after ASIC rejects one; each attempt is a
    /// page load, half a minute apart.
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
                    if (attempts < Math.Max(1, maxAttempts))
                    {
                        Report($"ASIC scored the token too low; waiting {RetryBackoff.TotalSeconds:0} s before a fresh one");
                        await Task.Delay(RetryBackoff, ct);
                    }
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
                    attempts, transient: true, captchaRejected: true);
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
            try { if (Context != null) await Context.CloseAsync(); } catch { /* closing, or already closed by the operator */ }
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
        var channels = _channel != null ? [_channel] : DefaultChannels;
        var failures = new List<string>();
        foreach (var channel in channels)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                session.Context = await session.Playwright.Chromium.LaunchPersistentContextAsync(ProfileDirectory, new()
                {
                    Channel = channel,
                    Headless = false,
                    IgnoreDefaultArgs = ["--enable-automation"],
                    Args = ["--disable-blink-features=AutomationControlled", "--window-size=1100,900", "--no-first-run", "--no-default-browser-check"],
                    ViewportSize = ViewportSize.NoViewport,
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

    /// <summary>Google's session cookie is only there once someone has signed in on this profile.</summary>
    private static async Task<bool> SignedInToGoogleAsync(IBrowserContext context)
    {
        try
        {
            var cookies = await context.CookiesAsync([GoogleSignInUrl]);
            return cookies.Any(c => c.Name is "SID" or "__Secure-1PSID" or "__Secure-3PSID");
        }
        catch (PlaywrightException)
        {
            return false;
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
