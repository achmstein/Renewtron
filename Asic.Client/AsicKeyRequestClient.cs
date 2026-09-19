using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;

namespace Asic.Client;

/// <summary>
/// ASIC's "Online Enquiry" form is three server-rendered pages sharing a session token in
/// the URL:
///   1. landingPage  — two dropdowns ("Business Name", "Maintain information") plus a
///                     reCAPTCHA v3 token that ASIC scores server-side (minimum 0.5).
///   2. inquiryDetails — multipart: question, entity number/name, contact details, two
///                     declaration checkboxes. No captcha of its own.
///   3. Thank You    — "Reference Number: 123456789".
/// A low captcha score bounces page 1 back with "CAPTCHA validation failed", so page 1 is
/// retried with a fresh token up to the caller's limit. Cookies live per instance, so
/// register this transient: one instance, one enquiry.
/// </summary>
public sealed class AsicKeyRequestClient : IAsicKeyRequestClient
{
    public const string BaseUrl = "https://www.edge.asic.gov.au/008/";
    public const string LandingPath = "inquiryV001?start/landingPage";
    public const string DefaultSiteKey = "6LcNpCYsAAAAAAW9cenu0V-SoImOZCQpUxyllHEf";
    public const string CaptchaAction = "submit";

    private const string FormName = "inquiryv001";
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
    private const string DefaultCaptchaField = "message-1-workArea-1-textField5-1";

    private const string Type1Value = "Business Name";
    private const string Type2Value = "Maintain information";

    private static readonly Regex SiteKeyRegex = new(@"recaptcha/api\.js\?render=([\w-]+)", RegexOptions.Compiled);
    private static readonly Regex ReferenceRegex = new(@"Reference\s+Number:?\s*(\d{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CaptchaScoreRegex = new(@"Score\s*:\s*([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private HttpClient _http;
    private readonly HtmlParser _parser = new();
    private readonly ICaptchaSolver _captcha;

    public AsicKeyRequestClient(ICaptchaSolver captcha)
    {
        _captcha = captcha;
    }

    // One enquiry = one ASIC session: a fresh cookie jar per call so a service that submits
    // several requests in a row never carries one enquiry's session token into the next.
    private static HttpClient CreateHttpClient(string proxyUrl)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var proxy = CreateProxy(proxyUrl);
        if (proxy != null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(120),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-AU,en;q=0.9");
        return http;
    }

    /// <summary>http://user:pass@host:port → WebProxy with credentials; null when blank or unparseable.</summary>
    private static WebProxy CreateProxy(string proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl)) return null;
        if (!Uri.TryCreate(proxyUrl.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException($"Proxy URL '{proxyUrl}' is not a valid absolute URL (expected http://user:pass@host:port).");

        var proxy = new WebProxy(new UriBuilder(uri) { UserName = "", Password = "" }.Uri) { BypassProxyOnLocal = false };
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        return proxy;
    }

    public async Task<string> GetEgressIpAsync(string proxyUrl = null, CancellationToken ct = default)
    {
        using var http = CreateHttpClient(proxyUrl);
        http.Timeout = TimeSpan.FromSeconds(30);
        using var response = await http.GetAsync("https://api.ipify.org", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(ct)).Trim();
    }

    public async Task<AsicKeyRequestResult> SubmitAsync(AsicKeyRequestInput input, double minCaptchaScore, int maxCaptchaAttempts, string proxyUrl = null, CancellationToken ct = default)
    {
        if (!_captcha.IsConfigured)
            return AsicKeyRequestResult.Failed("No captcha solver API key is configured (Settings → ASIC key requests).", 0, transient: false);

        var captchaAttempts = 0;
        HttpClient http;
        try
        {
            http = CreateHttpClient(proxyUrl);
        }
        catch (ArgumentException ex)
        {
            return AsicKeyRequestResult.Failed(ex.Message, 0, transient: false);
        }
        using var _ = http;
        _http = http;
        try
        {
            // ---- Page 1: landing ------------------------------------------------------
            var (landingHtml, landingUrl) = await GetAsync(LandingPath, ct);
            var page = ParsePage(landingHtml, landingUrl);
            if (page.Form == null)
                return AsicKeyRequestResult.Failed("ASIC enquiry landing page had no form — the site may be down or changed.", 0, transient: true);

            var siteKey = SiteKeyRegex.Match(landingHtml) is { Success: true } m ? m.Groups[1].Value : DefaultSiteKey;
            var captchaField = page.Form.QuerySelector("input#g-recaptcha-response")?.GetAttribute("name") ?? DefaultCaptchaField;

            ParsedPage detailsPage = null;
            string lastError = null;
            var attempts = Math.Max(1, maxCaptchaAttempts);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                captchaAttempts++;
                var solve = await _captcha.SolveRecaptchaV3Async(siteKey, page.Url, CaptchaAction, minCaptchaScore, ct);
                if (!solve.Succeeded)
                    return AsicKeyRequestResult.Failed($"Captcha solve failed: {solve.Error}", captchaAttempts, transient: true);

                var fields = HiddenFields(page.Form);
                fields[FieldType1] = Type1Value;
                fields[FieldType2] = Type2Value;
                fields[captchaField] = solve.Token;
                fields["x"] = "10";
                fields["y"] = "10";

                var (html, url) = await PostFormAsync(page.Action, new FormUrlEncodedContent(fields), page.Url, ct);
                var next = ParsePage(html, url);

                if (next.Action != null && next.Action.Contains("inquiryDetails", StringComparison.OrdinalIgnoreCase))
                {
                    detailsPage = next;
                    break;
                }

                var errors = ErrorsOn(next.Document);
                var captchaError = errors.FirstOrDefault(e => e.Contains("CAPTCHA", StringComparison.OrdinalIgnoreCase));
                if (captchaError != null)
                {
                    // "CAPTCHA validation failed, Score :0.1; minimum score require : 0.5" — buy another token.
                    lastError = captchaError;
                    page = next.Form != null ? next : page;
                    continue;
                }

                var summary = errors.Count > 0 ? string.Join("; ", errors) : "unexpected response from ASIC after the enquiry type page";
                return AsicKeyRequestResult.Failed($"ASIC rejected the enquiry type page: {summary}", captchaAttempts, transient: errors.Count == 0);
            }

            if (detailsPage == null)
            {
                // Keep ASIC's own wording: "Score :0.1; minimum score require : 0.5" says the token
                // was weak, while "The response parameter is invalid" or a 0.0 score says the
                // token was refused outright — different problems, different fixes.
                var score = lastError != null && CaptchaScoreRegex.Match(lastError) is { Success: true } sm ? sm.Groups[1].Value : null;
                var refused = score == null || score.TrimEnd('0', '.') is "" or "0";
                return AsicKeyRequestResult.Failed(
                    $"ASIC did not accept the captcha after {captchaAttempts} attempt(s). ASIC said: {lastError ?? "no token accepted"}",
                    captchaAttempts,
                    // A weak score can improve on a retry; a refused token is systematic (IP, key, or
                    // format) and retrying only spends captcha credit.
                    transient: !refused);
            }

            // ---- Page 2: enquiry details ---------------------------------------------
            var content = new MultipartFormDataContent();
            foreach (var (name, value) in HiddenFields(detailsPage.Form))
                content.Add(new StringContent(value), name);
            content.Add(new StringContent(Type1Value), FieldType1);
            content.Add(new StringContent(Type2Value), FieldType2);
            content.Add(new StringContent(input.Question ?? ""), FieldQuestion);
            content.Add(new StringContent(DigitsOnly(input.Abn)), FieldEntityNumber);
            content.Add(new StringContent(Truncate(input.BusinessName, 200)), FieldEntityName);
            content.Add(new StringContent(Truncate(input.GivenNames, 140)), FieldGivenNames);
            content.Add(new StringContent(Truncate(input.FamilyName, 40)), FieldFamilyName);
            content.Add(new StringContent(Truncate(input.PhonePrefix, 4)), FieldPhonePrefix);
            content.Add(new StringContent(Truncate(input.PhoneNumber, 15)), FieldPhoneNumber);
            content.Add(new StringContent(Truncate(input.Email, 50)), FieldEmail);
            content.Add(new StringContent(Truncate(input.Email, 50)), FieldConfirmEmail);
            content.Add(new StringContent("false"), FieldNeedAttachments);
            for (var i = 1; i <= 4; i++)
            {
                // A browser posts the empty attachment rows too; mirror it so the server-side
                // form binder sees the shape it expects.
                content.Add(new StringContent(""), $"message-1-formData-1-attachments-1-attachment-{i}-description-1");
                var empty = new ByteArrayContent([]);
                empty.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(empty, $"message-1-formData-1-attachments-1-attachment-{i}-inlineAttachment-1", "");
            }
            content.Add(new StringContent("true"), FieldDeclaresAuthorised);
            content.Add(new StringContent("true"), FieldDeclaresPrivacy);
            content.Add(new StringContent("10"), "x");
            content.Add(new StringContent("10"), "y");

            var (thanksHtml, thanksUrl) = await PostFormAsync(detailsPage.Action, content, detailsPage.Url, ct);
            var reference = ReferenceRegex.Match(thanksHtml);
            if (reference.Success)
                return AsicKeyRequestResult.Succeeded(reference.Groups[1].Value, captchaAttempts);

            var thanks = ParsePage(thanksHtml, thanksUrl);
            var detailErrors = ErrorsOn(thanks.Document);
            var message = detailErrors.Count > 0
                ? string.Join("; ", detailErrors)
                : thanks.Document.Title?.Contains("Thank", StringComparison.OrdinalIgnoreCase) == true
                    ? "ASIC accepted the enquiry but no reference number was found on the receipt."
                    : "unexpected response from ASIC after the details page";
            // Validation errors describe our data (not transient); anything else may be a blip.
            return AsicKeyRequestResult.Failed($"ASIC rejected the enquiry details: {message}", captchaAttempts, transient: detailErrors.Count == 0);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return AsicKeyRequestResult.Failed("ASIC request timed out.", captchaAttempts, transient: true);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            return AsicKeyRequestResult.Failed($"ASIC request failed: {ex.Message}", captchaAttempts, transient: true);
        }
        catch (Exception ex)
        {
            return AsicKeyRequestResult.Failed($"Unexpected error: {ex.Message}", captchaAttempts, transient: false);
        }
    }

    // ---- HTTP helpers ---------------------------------------------------------------

    private async Task<(string Html, string Url)> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        return (html, (response.RequestMessage?.RequestUri ?? new Uri(_http.BaseAddress, path)).ToString());
    }

    private async Task<(string Html, string Url)> PostFormAsync(string action, HttpContent content, string referer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_http.BaseAddress, action)) { Content = content };
        request.Headers.Referrer = new Uri(referer);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        return (html, (response.RequestMessage?.RequestUri ?? request.RequestUri).ToString());
    }

    // ---- Page parsing ---------------------------------------------------------------

    private sealed class ParsedPage
    {
        public IHtmlDocument Document;
        public IHtmlFormElement Form;
        public string Action;
        public string Url;
    }

    private ParsedPage ParsePage(string html, string url)
    {
        var doc = _parser.ParseDocument(html);
        var form = doc.Forms.FirstOrDefault(f => string.Equals(f.Name, FormName, StringComparison.OrdinalIgnoreCase))
                   ?? doc.Forms.FirstOrDefault();
        return new ParsedPage
        {
            Document = doc,
            Form = form,
            Action = form?.GetAttribute("action"),
            Url = url,
        };
    }

    private static Dictionary<string, string> HiddenFields(IHtmlFormElement form)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in form.QuerySelectorAll("input[type=hidden][name]").OfType<IHtmlInputElement>())
            fields[input.Name] = input.Value ?? "";
        return fields;
    }

    private static List<string> ErrorsOn(IHtmlDocument doc) =>
        doc.QuerySelectorAll(".error")
            .Select(e => Regex.Replace(e.TextContent ?? "", @"\s+", " ").Trim())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

    private static string DigitsOnly(string value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static string Truncate(string value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..max];
    }
}
