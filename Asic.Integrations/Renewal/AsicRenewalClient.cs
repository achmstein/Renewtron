using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using Asic.Integrations.Payment;
using Asic.Integrations.Shared;

namespace Asic.Integrations.Renewal;

/// <summary>
/// ASIC's public business name renewal (asicconnect.asic.gov.au/public/faces/renewal), driven
/// over HTTP the way a browser would: start an ADF session, enter the ABN, pick the name by its
/// account number, choose 1 or 3 years, declare, give an email, then pay by card through ASIC's
/// payment gateway or lodge to pay later by BPAY. The steps are in AsicRenewalClient.Steps.cs;
/// how each page is read is in <see cref="RenewalPages"/>.
///
/// One instance is one session (its own cookie jar), used one call at a time.
/// </summary>
public sealed partial class AsicRenewalClient : IAsicRenewalClient, IDisposable
{
    public const string BaseUrl = "https://asicconnect.asic.gov.au/";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0";
    private const string RenewalPath = "public/faces/renewal";

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly HtmlParser _htmlParser = new();
    private readonly IAsicPaymentClient _paymentClient;

    public AsicRenewalClient(IAsicPaymentClient paymentClient)
        : this(paymentClient, new CookieContainer(), handler: null, delay: null)
    {
    }

    /// <summary>Tests: a scripted <paramref name="handler"/> in place of ASIC, and no real waiting between retries.</summary>
    internal AsicRenewalClient(IAsicPaymentClient paymentClient, CookieContainer cookies, HttpMessageHandler? handler, Func<TimeSpan, Task>? delay)
    {
        _cookies = cookies;
        _delay = delay ?? Task.Delay;
        _paymentClient = paymentClient;
        _http = AsicHttp.CreateClient(handler ?? AsicHttp.CreateHandler(new AsicHttpSettings { Cookies = cookies }),
            BaseUrl, TimeSpan.FromSeconds(300), UserAgent);
    }

    /// <summary>Closes this client's connections. The container disposes it with its scope.</summary>
    public void Dispose() => _http.Dispose();

    /* ───────────── Search ───────────── */

    /// <summary>Messages Renewtron treats as worth retrying (see AsicSearchCoordinator.IsTransient).</summary>
    private const string SearchFailedPrefix = "Search failed:";
    private const string SessionFailed = "Failed to initialize renewal session";

    public async Task<BusinessNamesResult> SearchByAbnAsync(string abn)
    {
        // Read-only, so transient failures (network blips, maintenance pages that break the
        // session) are retried in place.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var result = await SearchByAbnOnceAsync(abn);
            if (result.Success || !IsTransient(result) || attempt == maxAttempts) return result;
            await _delay(TimeSpan.FromSeconds(attempt * 3));
        }
    }

    private static bool IsTransient(BusinessNamesResult result)
    {
        var message = result.ErrorMessage ?? "";
        return message.StartsWith(SearchFailedPrefix) || message == SessionFailed;
    }

    private async Task<BusinessNamesResult> SearchByAbnOnceAsync(string abn)
    {
        try
        {
            ResetSession();
            var (sessionId, windowId, viewState) = await StartSessionAsync();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(windowId) || string.IsNullOrEmpty(viewState))
                return BusinessNamesResult.Failed(SessionFailed);

            var (response, content) = await PostAsync(windowId, AbnForm(abn, windowId, viewState), adsPageId: "3");
            if (!response.IsSuccessStatusCode || !RenewalPages.IsNameList(content))
                return BusinessNamesResult.Failed(RenewalPages.Refusal(content)?.Message
                    ?? "Failed to search for business names. Please check the ABN and try again.");

            var names = RenewalPages.Names(content);
            return names.Count == 0
                ? BusinessNamesResult.Failed("No business names found for this ABN")
                : BusinessNamesResult.Succeeded(names);
        }
        catch (Exception ex)
        {
            return BusinessNamesResult.Failed($"{SearchFailedPrefix} {ex.Message}");
        }
    }

    /* ───────────── Renewal ───────────── */

    public async Task<RenewalResult> RenewBusinessNameAsync(string abn, string accountNumber, int renewalYears, string email, CreditCardDetails cardDetails, bool payLater = false)
    {
        try
        {
            ResetSession();
            var data = RenewalStepData.Create(abn, accountNumber, renewalYears, email, cardDetails);

            // Each step runs only if the one before it succeeded; the first failure is the result.
            var upToPayment = StartSessionStepAsync(data)
                .ThenAsync("Submit ABN", SubmitAbnStepAsync)
                .ThenAsync("Select Business", SelectBusinessStepAsync)
                .ThenAsync("Proceed to Renewal Period", ProceedToRenewalPeriodStepAsync)
                .ThenAsync("Select Renewal Period", SelectRenewalPeriodStepAsync)
                .ThenAsync("Proceed to Review", ProceedToReviewStepAsync)
                .ThenAsync("Select Authority Declaration", SelectAuthorityDeclarationStepAsync)
                .ThenAsync("Proceed to Payment", ProceedToPaymentStepAsync)
                .ThenAsync("Submit Email", SubmitEmailStepAsync);

            if (payLater)
            {
                // ASIC's "Pay later -> BPAY": lodge the renewal now, pay at the bank later.
                return await upToPayment
                    .ThenAsync("Select BPAY", SelectBpayStepAsync)
                    .ThenAsync("Submit Pay Later", SubmitPayLaterStepAsync)
                    .ToRenewalResultAsync(d => new RenewalResult
                    {
                        IsSuccess = true,
                        Message = $"Lodged with ASIC to pay later by BPAY (biller {d.BpayBillerCode}, reference {d.BpayReference})",
                        TransactionReference = d.TransactionReference,
                        BpayBillerCode = d.BpayBillerCode,
                        BpayReference = d.BpayReference,
                    });
            }

            return await upToPayment
                .ThenAsync("Select Payment Method", SelectPaymentMethodStepAsync)
                .ThenAsync("Open Payment Gateway", OpenPaymentGatewayStepAsync)
                .ThenAsync("Process Payment", ProcessPaymentStepAsync)
                .ToRenewalResultAsync(d => RenewalResult.Success(d.TransactionReference, d.HostedTokenizationId));
        }
        catch (Exception ex)
        {
            return RenewalResult.Failed($"Renewal failed: {ex.Message}", "Exception");
        }
    }

    /* ───────────── Session ───────────── */

    private void ResetSession() => AsicHttp.ExpireCookies(_cookies, _http.BaseAddress!);

    /// <summary>
    /// Opens the renewal page and answers ADF's loopback. Any part can come back empty (a
    /// maintenance page, a missing cookie); callers treat that as a failed session.
    /// </summary>
    private async Task<(string? SessionId, string? WindowId, string? ViewState)> StartSessionAsync()
    {
        var response = await _http.GetAsync(RenewalPath);
        var content = await response.Content.ReadAsStringAsync();

        // The session id is in Set-Cookie, which is absent when ASIC serves an error page or
        // reuses the existing cookie: fall back to the jar.
        string? sessionId = null;
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            sessionId = setCookies.FirstOrDefault(x => x.StartsWith("JSESSIONID="))?.Split(';')[0].Replace("JSESSIONID=", "");
        sessionId ??= _cookies.GetCookies(_http.BaseAddress!).FirstOrDefault(c => c.Name == "JSESSIONID" && !c.Expired)?.Value;

        // An unreadable loopback is sent blank, and the session fails below.
        response = await _http.GetAsync($"{RenewalPath};jsessionid={sessionId}?{Adf.LoopbackQuery(Adf.LoopId(content), Adf.ScriptWindowId(content))}");
        content = await response.Content.ReadAsStringAsync();

        var (windowId, viewState) = Adf.HiddenFields(await _htmlParser.ParseDocumentAsync(content));
        return (sessionId, windowId, viewState);
    }

    /* ───────────── Posting a step ───────────── */

    /// <summary>Posts one step's form to the renewal page as an ADF partial request.</summary>
    private async Task<(HttpResponseMessage Response, string Content)> PostAsync(string? windowId, string form, string adsPageId = "2")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{RenewalPath}?Adf-Window-Id={windowId}&Adf-Page-Id=1")
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", adsPageId);
        var response = await _http.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    // The page header's search box, posted (empty) with every form on the page.
    private static readonly string[] HeaderSearchFields =
    [
        "tmpt:connectHeaderView:searchWithinDropDown=",
        "tmpt:connectHeaderView:searchForNeedle=",
        "tmpt:connectHeaderView:searchForNeedle2=",
        "tmpt:connectHeaderView:searchForNeedle3=",
    ];

    private const string FormId = "org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform";
    private const string RenderRegion = "oracle.adf.view.rich.RENDER=tmpt%3Aregion";
    private const string WaitPopupShown = "oracle.adf.view.rich.DELTAS=%7Btmpt%3Awaitpopup%3D%7B_shown%3D%7D%7D";

    /// <summary>A form body: the header's search fields, then <paramref name="fields"/> ("name=value", values already encoded), in order.</summary>
    private static string Form(IEnumerable<string> fields) => string.Join("&", HeaderSearchFields.Concat(fields));

    /// <summary>The form id and the session's window, page and view state, in the order every step but the ABN one sends them.</summary>
    private static string[] Session(RenewalStepData data) =>
        [FormId, $"Adf-Window-Id={data.AdfWindowId}", "Adf-Page-Id=1", $"javax.faces.ViewState={data.ViewState}"];

    /// <summary>The ABN form: the same in a search and a renewal (only the request header differs).</summary>
    private static string AbnForm(string abn, string? windowId, string? viewState) => Form(
    [
        "tmpt:region:0:form:accInput=",
        $"tmpt:region:0:form:it2={abn}",
        FormId,
        $"Adf-Window-Id={windowId}",
        $"javax.faces.ViewState={viewState}",
        "Adf-Page-Id=1",
        .. Click("tmpt:region:0:form:nextButt"),
    ]);

    /// <summary>A button press on <paramref name="component"/> (e.g. tmpt:region:2:form:nextButt).</summary>
    private static string[] Click(string component) =>
    [
        $"event={Encode(component)}",
        $"event.{component}={Adf.ActionEvent}",
        $"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2C{Encode(component)}",
    ];

    /// <summary>A change of one or more auto-submitting radios, fired together as a browser does.</summary>
    private static string[] ValueChange(params string[] components) =>
    [
        $"event={string.Join("%2C", components.Select(Encode))}",
        .. components.Select(c => $"event.{c}={Adf.ValueChangeEvent}"),
        $"oracle.adf.view.rich.PROCESS={string.Join("%2C", components.Select(Encode))}",
    ];

    private static string Encode(string component) => Uri.EscapeDataString(component);
}
