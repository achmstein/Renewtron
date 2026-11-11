#nullable enable
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Asic.Integrations;

/// <summary>A page of ASIC Connect as we last saw it: where it is and the ADF state its next post needs.</summary>
public sealed record AsicPortalPage(string Url, string Html, string? WindowId, string? ViewState)
{
    /// <summary>The home page's "Link a business/request ASIC key" action: only there when logged in.</summary>
    public bool IsLoggedInHome => Html.Contains("Link a business/request ASIC", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Who we say we are on an ASIC Connect "Declare" page (the authorityOption radio).
/// </summary>
public enum AsicDeclareAs
{
    /// <summary>"I am submitting this form on behalf of the registration or licence holder and am authorised to do so" (authority4).</summary>
    OnBehalfOfHolder,
    /// <summary>"I am the registration or licence holder" (authority3): what David's script ticks.</summary>
    Holder,
}

public static class AsicDeclare
{
    /// <summary>The Declare page's radio group and its two boxes, as served on 7 Nov 2026.</summary>
    public const string AuthorityField = "authorityOption";
    public const string Declaration1 = "declaration1";
    public const string Declaration2 = "declaration2";

    /// <summary>The authorityOption value for <paramref name="who"/>.</summary>
    public static string AuthorityValue(AsicDeclareAs who) => who switch
    {
        AsicDeclareAs.Holder => "authority3",
        _ => "authority4",
    };

    /// <summary>The fields to post with the Declare page's Submit.</summary>
    public static Dictionary<string, string> Fields(AsicDeclareAs who) => new()
    {
        [Declaration1] = "t",
        [Declaration2] = "t",
        [AuthorityField] = AuthorityValue(who),
    };
}

public sealed record AsicPortalLoginResult(bool Ok, string? Error, AsicPortalPage? Home)
{
    public static AsicPortalLoginResult Failed(string error) => new(false, error, null);
}

/// <summary>
/// Our agent login to ASIC Connect over plain HTTP, the same way <see cref="AsicRenewalClient"/>
/// drives the public renewal pages. The login itself isn't ADF: ASIC Connect's protected pages
/// redirect to Oracle Access Manager at auth.asic.gov.au, whose login.jsp is an ordinary form
/// (userid, password, request_id) posted to /oam/server/auth_cred_submit; OAM then redirects back.
///
/// One instance is one session (its own cookie jar). ASIC allows one session per account, so
/// the caller keeps a single instance and uses it one request at a time.
/// </summary>
public sealed partial class AsicPortalClient : IDisposable
{
    public const string BaseUrl = "https://asicconnect.asic.gov.au/";
    public const string HomePath = "emeraldCONNECT/faces/HomePage";
    public const string AuthHost = "auth.asic.gov.au";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0";

    private readonly HttpClientHandler _handler;
    private readonly HttpClient _http;
    private readonly HtmlParser _parser = new();

    public AsicPortalClient()
    {
        _handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            // Redirects are followed by hand (SendAsync below): ASIC sometimes redirects to its own
            // pages over http://, which .NET won't follow from https; a browser just upgrades them.
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(180) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-AU,en;q=0.9");
    }

    /// <summary>Optional: called with each ADF event's component id and ASIC's answer (for diagnostics).</summary>
    public Action<string, string>? Trace { get; set; }

    /// <summary>The page we're on, after the last request.</summary>
    public AsicPortalPage? Current { get; private set; }

    /// <summary>
    /// Logs in from scratch: open the home page (OAM sends us to its login form), post the
    /// username and password, and follow OAM back to the home page.
    /// </summary>
    public async Task<AsicPortalLoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        ForgetSession();

        var (loginUrl, loginHtml) = await GetAsync(HomePath, ct);
        if (!IsAuthUrl(loginUrl))
        {
            // Already in (shouldn't happen with an empty cookie jar), or ASIC is down.
            var home = await FinishAdfPageAsync(loginUrl, loginHtml, ct);
            return home.IsLoggedInHome
                ? new AsicPortalLoginResult(true, null, home)
                : AsicPortalLoginResult.Failed("ASIC Connect didn't show its login page. It may be down; try again later.");
        }

        var form = ParseLoginForm(loginHtml);
        if (form is null) return AsicPortalLoginResult.Failed("ASIC's login page has changed: no login form found.");

        var fields = new Dictionary<string, string>(form.Hidden)
        {
            ["userid"] = username.Trim(),
            ["password"] = password,
        };
        var action = new Uri(new Uri(loginUrl), form.Action);
        var post = new HttpRequestMessage(HttpMethod.Post, action) { Content = new FormUrlEncodedContent(fields) };
        post.Headers.Referrer = new Uri(loginUrl);
        var (url, html) = await SendAsync(post, ct);

        if (IsAuthUrl(url))
            return AsicPortalLoginResult.Failed(LoginError(url, html));

        var page = await FinishAdfPageAsync(url, html, ct);
        if (!page.IsLoggedInHome && !url.Contains(HomePath, StringComparison.OrdinalIgnoreCase))
            page = await OpenAsync(HomePath, ct) ?? page;
        return page.IsLoggedInHome
            ? new AsicPortalLoginResult(true, null, page)
            : AsicPortalLoginResult.Failed("Logged in, but ASIC Connect's home page didn't show the \"Link a business\" action.");
    }

    /// <summary>
    /// Opens a logged-in page (an ASIC Connect path or full URL). Null when OAM sent us to the
    /// login page instead: the session is gone.
    /// </summary>
    public async Task<AsicPortalPage?> OpenAsync(string pathOrUrl, CancellationToken ct = default)
    {
        var (url, html) = await GetAsync(pathOrUrl, ct);
        if (IsAuthUrl(url)) return null;
        return await FinishAdfPageAsync(url, html, ct);
    }

    /// <summary>Still logged in? Opens the home page to find out.</summary>
    public async Task<bool> IsLoggedInAsync(CancellationToken ct = default) =>
        (await OpenAsync(HomePath, ct))?.IsLoggedInHome == true;

    /// <summary>Ends the session at ASIC (best effort) and forgets our cookies.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await GetAsync($"https://{AuthHost}/oam/server/logout?end_url=" +
                           Uri.EscapeDataString(BaseUrl + "public/faces/landingPage"), ct);
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        ForgetSession();
    }

    private Task<(string Url, string Html)> GetAsync(string pathOrUrl, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, pathOrUrl), ct);

    /// <summary>
    /// Sends the request and follows redirects like a browser: a 302/303 after a POST becomes a
    /// GET, and ASIC's own http:// links are upgraded to https://. Returns where we ended up.
    /// </summary>
    private async Task<(string Url, string Html)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var hop = 0; hop < 20; hop++)
        {
            using var response = await _http.SendAsync(request, ct);
            var url = response.RequestMessage?.RequestUri?.ToString() ?? request.RequestUri?.ToString() ?? "";
            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(new Uri(url), location);
                if (next.Scheme == Uri.UriSchemeHttp && next.Host.EndsWith("asic.gov.au", StringComparison.OrdinalIgnoreCase))
                    next = new UriBuilder(next) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
                var keepMethod = code is 307 or 308;
                var follow = new HttpRequestMessage(keepMethod ? request.Method : HttpMethod.Get, next);
                if (keepMethod && request.Content is not null)
                    follow.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(ct))
                    {
                        Headers = { ContentType = request.Content.Headers.ContentType },
                    };
                follow.Headers.Referrer = new Uri(url);
                request.Dispose();
                request = follow;
                continue;
            }
            var html = await response.Content.ReadAsStringAsync(ct);
            request.Dispose();
            return (url, html);
        }
        request.Dispose();
        throw new HttpRequestException("ASIC Connect redirected too many times.");
    }

    /// <summary>
    /// An ADF page's first answer is a small "loopback" script that reloads it with the window
    /// id it chose; do that reload, then read the window id and view state off the real page.
    /// </summary>
    private async Task<AsicPortalPage> FinishAdfPageAsync(string url, string html, CancellationToken ct)
    {
        if (ParseLoopback(html) is { } loop)
        {
            var path = new Uri(url).GetLeftPart(UriPartial.Path);
            var separator = path.Contains(";jsessionid=", StringComparison.OrdinalIgnoreCase) ? "" : loop.SessionSuffix;
            var reload = $"{path}{separator}?_afrLoop={loop.Loop}&_afrWindowMode=0&Adf-Window-Id={loop.WindowId}" +
                         "&_afrFS=16&_afrMT=screen&_afrMFW=1865&_afrMFH=926&_afrMFDW=1920&_afrMFDH=1080&_afrMFC=8&_afrMFCI=0&_afrMFM=0&_afrMFR=96&_afrMFG=0&_afrMFS=0&_afrMFO=0";
            (url, html) = await GetAsync(reload, ct);
        }

        var doc = await _parser.ParseDocumentAsync(html, ct);
        var page = new AsicPortalPage(
            url,
            html,
            doc.QuerySelector("input[name='Adf-Window-Id']")?.GetAttribute("value"),
            doc.QuerySelector("input[name='javax.faces.ViewState']")?.GetAttribute("value"));
        Current = page;
        return page;
    }

    private void ForgetSession()
    {
        foreach (var host in new[] { new Uri(BaseUrl), new Uri($"https://{AuthHost}/") })
            foreach (Cookie cookie in _handler.CookieContainer.GetCookies(host))
                cookie.Expired = true;
        Current = null;
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    /* ───────────── ADF events (confirmed live, 7 Nov 2026) ───────────── */

    /// <summary>The XML ADF sends with a button or link click.</summary>
    public const string ActionPayload = "<m xmlns=\"http://oracle.com/richClient/comm\"><k v=\"type\"><s>action</s></k></m>";

    /// <summary>
    /// Lodgements &amp; Notifications row radio (confirmed live, 7 Nov 2026): the page's handleStateChange
    /// cancels the radio's own change and queues this custom event instead; its answer fills the row's
    /// action list (Change business address = 1, Change holder details = 2, Change representative details = 3).
    /// Wizard buttons are sent with PROCESS "&lt;form id&gt;,&lt;button id&gt;" (e.g. "view,saveEmailBtn");
    /// Cancel opens a popup confirmed with event "yes", PROCESS "popup,yes".
    /// </summary>
    public const string RowSelectPayload = "<m xmlns=\"http://oracle.com/richClient/comm\"><k v=\"_custom\"><b>1</b></k><k v=\"paramLodgment\"><b>1</b></k><k v=\"immediate\"><b>1</b></k><k v=\"type\"><s>radioSelectEvent</s></k></m>";

    /// <summary>The XML ADF sends when an autoSubmit field (radio, list) changes.</summary>
    public const string ValueChangePayload = "<m xmlns=\"http://oracle.com/richClient/comm\"><k v=\"autoSubmit\"><b>1</b></k><k v=\"suppressMessageShow\"><s>true</s></k><k v=\"type\"><s>valueChange</s></k></m>";

    /// <summary>
    /// Sends an ADF partial event from <paramref name="page"/> the way ADF's JavaScript does: posts
    /// the page's form with event=&lt;id&gt; and returns the partial response. ADF hands out a new
    /// view state with each answer; the returned page carries it, so chain events on the returned
    /// page. A click that navigates answers with &lt;redirect url="…"/&gt; (see <see cref="ClickAndFollowAsync"/>).
    /// </summary>
    public async Task<(AsicPortalPage Page, string Response)> PostEventAsync(AsicPortalPage page, string componentId,
        IReadOnlyDictionary<string, string>? values = null, string payload = ActionPayload, string? process = null, CancellationToken ct = default)
    {
        var (target, fields) = BuildPost(page, componentId, values, payload);
        fields["oracle.adf.view.rich.PROCESS"] = process ?? componentId;
        var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "2");
        request.Headers.Referrer = new Uri(page.Url);
        var (_, body) = await SendAsync(request, ct);
        Trace?.Invoke(componentId, body);
        var viewState = ParsePartialViewState(body);
        return (viewState is null ? page : page with { ViewState = viewState }, body);
    }

    /// <summary>
    /// A command without partialSubmit (most "Next", "Submit", "Link to Account" buttons): ADF posts
    /// the whole form as an ordinary page request and the answer is the next page. Null when the
    /// session is gone.
    /// </summary>
    public async Task<AsicPortalPage?> SubmitPageAsync(AsicPortalPage page, string componentId,
        IReadOnlyDictionary<string, string>? values = null, CancellationToken ct = default)
    {
        var (target, fields) = BuildPost(page, componentId, values, ActionPayload);
        var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Referrer = new Uri(page.Url);
        var (url, html) = await SendAsync(request, ct);
        if (IsAuthUrl(url)) return null;
        return await FinishAdfPageAsync(url, html, ct);
    }

    private static (string Target, Dictionary<string, string> Fields) BuildPost(AsicPortalPage page, string componentId,
        IReadOnlyDictionary<string, string>? values, string payload)
    {
        var form = ReadForm(page.Html, componentId)
            ?? throw new InvalidOperationException($"No form on {page.Url} holds {componentId}.");
        var fields = new Dictionary<string, string>(form.Fields);
        if (page.ViewState is not null) fields["javax.faces.ViewState"] = page.ViewState;
        if (values is not null) foreach (var (k, v) in values) fields[k] = v;
        fields["event"] = componentId;
        fields[$"event.{componentId}"] = payload;
        var action = new Uri(new Uri(page.Url), form.Action);
        var window = fields.GetValueOrDefault("Adf-Window-Id") ?? page.WindowId ?? "";
        return ($"{action.GetLeftPart(UriPartial.Path)}?Adf-Window-Id={Uri.EscapeDataString(window)}&Adf-Page-Id=1", fields);
    }

    /// <summary>Clicks a command that opens another page, and opens it. Null when the session is gone.</summary>
    public async Task<AsicPortalPage?> ClickAndFollowAsync(AsicPortalPage page, string componentId,
        IReadOnlyDictionary<string, string>? values = null, string? process = null, CancellationToken ct = default)
    {
        var (_, response) = await PostEventAsync(page, componentId, values, ActionPayload, process, ct);
        var redirect = ParseRedirect(response);
        return redirect is null ? null : await OpenAsync(redirect, ct);
    }

    /// <summary>Home → "Link a business/request ASIC key".</summary>
    public async Task<AsicPortalPage?> OpenLinkBusinessAsync(CancellationToken ct = default)
    {
        var home = await OpenAsync(HomePath, ct);
        if (home is null || !home.IsLoggedInHome) return null;
        var id = FindCommandId(home.Html, "Link a business/request ASIC") ?? "pt1:conRH:cmdLinkBN";
        return await ClickAndFollowAsync(home, id, ct: ct);
    }

    /// <summary>Home → "Lodgements &amp; Notifications": every business name on our account, one row each.</summary>
    public async Task<AsicPortalPage?> OpenLodgementsAsync(CancellationToken ct = default)
    {
        var home = await OpenAsync(HomePath, ct);
        if (home is null || !home.IsLoggedInHome) return null;
        // The tab's id is generated (pt1:hDer:head:j_id__ctru31pc2 today), so find it by its words.
        var id = FindCommandId(home.Html, "Lodgements & Notifications")
                 ?? throw new InvalidOperationException("No \"Lodgements & Notifications\" tab on the home page.");
        return await ClickAndFollowAsync(home, id, ct: ct);
    }

    /// <summary>
    /// The "Link a business" form, filled the way a person does it (confirmed live, 7 Nov 2026):
    /// "Yes, I have a key" and "Business Name" are autoSubmit choices that each send a partial
    /// event (the key fields only count once both are made), then date of birth, key and Registered
    /// Agent, and "Link to Account" posts the whole page. Returns the confirmation (or error) page;
    /// null when the session is gone. Classify its text with the link classifier.
    /// </summary>
    public async Task<AsicPortalPage?> SubmitLinkBusinessAsync(AsicPortalPage linkPage, string asicKey, string dateOfBirth, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string> { [LinkForm.HasKey] = LinkForm.HasKeyYes };
        var (page, _) = await PostEventAsync(linkPage, LinkForm.HasKeyYes, values, ValueChangePayload, ct: ct);
        values[LinkForm.Kind] = LinkForm.KindBusinessName;
        (page, _) = await PostEventAsync(page, LinkForm.Kind, values, ValueChangePayload, ct: ct);
        values[LinkForm.DateOfBirth] = dateOfBirth;
        values[LinkForm.Key] = asicKey;
        values[LinkForm.Role] = LinkForm.RoleRegisteredAgent;
        return await SubmitPageAsync(page, LinkForm.Submit, values, ct);
    }

    /// <summary>The link page's fields, as served on 7 Nov 2026 (page LinkBusness, form linkBNForm).</summary>
    public static class LinkForm
    {
        public const string FormId = "onBoardFrm";
        public const string HasKey = "hasKey";
        public const string HasKeyYes = "onBoardFrm:sbr3";
        public const string Kind = "onBoardFrm:linkBusinessId";
        public const string KindBusinessName = "0";
        public const string DateOfBirth = "onBoardFrm:sflinkBus:birthDate1";
        public const string Key = "onBoardFrm:sflinkBus:key";
        public const string Role = "onBoardFrm:sflinkBus:role";
        public const string RoleRegisteredAgent = "5";
        public const string Submit = "onBoardFrm:sflinkBus:linkBtn";
    }

    /* ───────────── Changing a linked name (confirmed live, 7 Nov 2026) ───────────── */

    /// <summary>The actions on a name's Lodgements row, as ASIC words them.</summary>
    public static class NameActions
    {
        public const string BusinessAddress = "Change business address";
        public const string HolderDetails = "Change holder details";
        public const string Representatives = "Change representative details";
    }

    /// <summary>
    /// What opening one of a name's actions gave: the action's page, or why not. <see cref="Offered"/>
    /// lists what ASIC offered for the name: when the change actions are missing, our access to it
    /// is gone (it happens when our own representative is removed).
    /// </summary>
    public sealed record NameActionResult(AsicPortalPage? Page, IReadOnlyList<string> Offered, string? Problem);

    /// <summary>
    /// Lodgements &amp; Notifications → the name's row → its action list → Go. The row's radio sends
    /// a custom event whose answer fills the action list; choosing an action is a value change; Go
    /// is an action that redirects to the action's page.
    /// </summary>
    public async Task<NameActionResult> OpenNameActionAsync(string businessName, string action, CancellationToken ct = default)
    {
        var list = await OpenLodgementsAsync(ct);
        if (list is null) return new(null, [], "The ASIC Connect session ended.");

        var radio = FindNameRowRadio(list.Html, businessName);
        if (radio is null) return new(null, [], $"\"{businessName}\" isn't on our ASIC account's Lodgements & Notifications list.");
        var prefix = radio[..(radio.Length - "maitainBNRadio".Length)];
        var values = new Dictionary<string, string> { ["radios"] = radio };

        var (page, response) = await PostEventAsync(list, radio, values, RowSelectPayload, ct: ct);
        var options = ParseOptions(response, prefix + "maintainDropdown");
        var offered = options.Keys.Where(k => k.Length > 0 && !k.StartsWith("Select", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!options.TryGetValue(action, out var value))
            return new(null, offered, $"ASIC doesn't offer \"{action}\" for this name (it offers: {string.Join(", ", offered)}).");

        values[prefix + "maintainDropdown"] = value;
        (page, _) = await PostEventAsync(page, prefix + "maintainDropdown", values, ValueChangePayload, ct: ct);
        var (_, go) = await PostEventAsync(page, prefix + "cmdButtonParent", values, ct: ct);
        var redirect = ParseRedirect(go);
        if (redirect is null) return new(null, offered, $"\"{action}\" didn't open a page.");
        var opened = await OpenAsync(redirect, ct);
        return opened is null ? new(null, offered, "The ASIC Connect session ended.") : new(opened, offered, null);
    }

    /// <summary>The result of a lodgement: ASIC's confirmation and its transaction reference, or what went wrong.</summary>
    public sealed record LodgeResult(bool Lodged, string? Reference, string? Problem, string PageText)
    {
        public static LodgeResult Failed(string problem, string pageText = "") => new(false, null, problem, pageText);
    }

    /// <summary>
    /// "Change business address" → the Email row's Edit → our email → Save → Next → Submit → Declare
    /// → Submit. Field and button ids are this wizard's own (view region, button_review_1,
    /// button_validate_2, button_submit_3, authorityOption).
    /// </summary>
    public async Task<LodgeResult> ChangeBusinessEmailAsync(AsicPortalPage wizard, string email, AsicDeclareAs declareAs, CancellationToken ct = default)
    {
        var row = FindEmailRow(wizard.Html);
        if (row is null) return LodgeResult.Failed("No Email row on the business address page.", PageText(wizard.Html));

        var page = wizard;
        var edit = $"tblAddr:{row}:cmdLink";
        (page, _) = await PostEventAsync(page, edit, process: $"view,{edit}", ct: ct);
        var (afterSave, saved) = await PostEventAsync(page, "saveEmailBtn", new Dictionary<string, string> { ["emailInput"] = email }, process: "view,saveEmailBtn", ct: ct);
        page = afterSave;
        var table = TableUpdateRe().Match(saved);
        if (!table.Success || !table.Groups[1].Value.Contains(email, StringComparison.OrdinalIgnoreCase))
        {
            await CancelWizardAsync(page, wizard.Html, "view", ct);
            return LodgeResult.Failed("ASIC didn't take the new email.", PartialText(saved));
        }

        (page, _) = await PostEventAsync(page, "button_review_1", process: "view,button_review_1", ct: ct);
        var (onDeclare, declare) = await PostEventAsync(page, "button_validate_2", process: "view,button_validate_2", ct: ct);
        page = onDeclare;
        var declareText = PartialText(declare);
        if (!Regex.IsMatch(declareText, "I declare that", RegexOptions.IgnoreCase))
        {
            await CancelWizardAsync(page, declare, "view", ct);
            return LodgeResult.Failed("Didn't reach the Declare page.", declareText);
        }

        var fields = AsicDeclare.Fields(declareAs);
        (page, _) = await PostEventAsync(page, AsicDeclare.AuthorityField, fields, ValueChangePayload, AsicDeclare.AuthorityField, ct);
        var (_, done) = await PostEventAsync(page, "button_submit_3", fields, process: "view,button_submit_3", ct: ct);
        return ReadConfirmation(done);
    }

    /// <summary>A representative on the "Change representative details" page: its words and its Remove link.</summary>
    public sealed record RepresentativeRow(int Index, string Text, string RemoveId);

    public static IReadOnlyList<RepresentativeRow> ReadRepresentatives(string html)
    {
        var rows = new List<RepresentativeRow>();
        foreach (Match m in RemoveRepRe().Matches(html))
        {
            // The row's own words sit just before its Remove link (name, then "Individual"/"Company").
            // Stop at the start of the Remove link's own tag (the match sits inside it).
            var tagStart = html.LastIndexOf('<', m.Index);
            var text = PageText(html[Math.Max(0, tagStart - 2500)..tagStart]);
            rows.Add(new(int.Parse(m.Groups[2].Value), text.Length > 160 ? text[^160..] : text, m.Groups[1].Value));
        }
        return rows;
    }

    /// <summary>
    /// "Change representative details" → the row's "Remove Representative" → Next → Submit →
    /// Declare (both boxes, and the declaration matching <paramref name="declareAs"/>) → Next.
    /// <paramref name="expectName"/> must be on the removal and review pages, so a shifted row
    /// can't remove the wrong person.
    /// </summary>
    public async Task<LodgeResult> RemoveRepresentativeAsync(AsicPortalPage repsPage, RepresentativeRow row, string expectName, AsicDeclareAs declareAs, CancellationToken ct = default)
    {
        var form = row.RemoveId[..row.RemoveId.IndexOf(':')];
        var wizard = await ClickAndFollowAsync(repsPage, row.RemoveId, process: $"{form},{row.RemoveId}", ct: ct);
        if (wizard is null) return LodgeResult.Failed("\"Remove Representative\" didn't open its page.");
        if (!PageText(wizard.Html).Contains(expectName, StringComparison.OrdinalIgnoreCase))
            return LodgeResult.Failed($"The removal page isn't for \"{expectName}\".", PageText(wizard.Html));

        var page = wizard;
        var next = FindButton(wizard.Html, "Next");
        if (next is null) return LodgeResult.Failed("No Next button on the removal page.", PageText(wizard.Html));
        var (onReview, review) = await PostEventAsync(page, next, process: $"{RegionOf(next)},{next}", ct: ct);
        page = onReview;
        var reviewText = PartialText(review);
        if (!reviewText.Contains("Review", StringComparison.OrdinalIgnoreCase) || !reviewText.Contains(expectName, StringComparison.OrdinalIgnoreCase))
        {
            await CancelWizardAsync(page, wizard.Html, RegionOf(next), ct);
            return LodgeResult.Failed("The review page didn't show this representative.", reviewText);
        }

        var submit = FindButton(review, "Submit");
        if (submit is null) return LodgeResult.Failed("No Submit button on the review page.", reviewText);
        var (onDeclare, declare) = await PostEventAsync(page, submit, process: $"{RegionOf(submit)},{submit}", ct: ct);
        page = onDeclare;
        var choice = ChooseDeclaration(declare, declareAs);
        if (choice is null)
        {
            await CancelWizardAsync(page, review, RegionOf(submit), ct);
            return LodgeResult.Failed("The Declare page wasn't as expected.", PartialText(declare));
        }

        foreach (var box in choice.Boxes)
            (page, _) = await PostEventAsync(page, box, choice.Fields, ValueChangePayload, box, ct);
        (page, _) = await PostEventAsync(page, choice.RadioId, choice.Fields, ValueChangePayload, choice.RadioId, ct);
        var finish = FindButton(declare, "Next") ?? FindButton(declare, "Submit");
        if (finish is null) return LodgeResult.Failed("No Next button on the Declare page.", PartialText(declare));
        var (_, done) = await PostEventAsync(page, finish, choice.Fields, process: $"{RegionOf(finish)},{finish}", ct: ct);
        return ReadConfirmation(done);
    }

    /// <summary>The holder's type on the "Change holder details" page ("Individual", "Body Corporate", …).</summary>
    public static string? HolderType(string html) =>
        HolderTypeRe().Match(PageText(html)) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    /// <summary>The declaration fields to post: every box ticked, and the authority radio for the chosen declaration.</summary>
    public sealed record DeclarationChoice(IReadOnlyList<string> Boxes, string RadioId, Dictionary<string, string> Fields);

    /// <summary>
    /// Reads a Declare page with radio values that are component ids (the representative wizards):
    /// its two tick boxes and its two authority radios, told apart by their words ("I am the …
    /// holder" vs "on behalf of the … holder"). Null when it isn't two boxes and that pair.
    /// </summary>
    public static DeclarationChoice? ChooseDeclaration(string declareHtml, AsicDeclareAs declareAs)
    {
        var inputs = DeclareInputRe().Matches(declareHtml).Select(m => (Name: m.Groups[1].Value, Type: m.Groups[2].Value, Value: m.Groups[3].Value)).ToList();
        var boxes = inputs.Where(i => i.Type == "checkbox").Select(i => i.Name).Distinct().ToList();
        var radios = inputs.Where(i => i.Type == "radio").ToList();
        if (boxes.Count != 2 || radios.Count != 2) return null;

        string Label(string id)
        {
            var i = declareHtml.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
            return i < 0 ? "" : PartialText(declareHtml[i..Math.Min(declareHtml.Length, i + 1500)]);
        }
        bool IsHolder(string id)
        {
            var l = Label(id);
            return HolderDeclRe().IsMatch(l) && !l[..Math.Min(160, l.Length)].Contains("on behalf", StringComparison.OrdinalIgnoreCase);
        }
        var holder = radios.Where(r => IsHolder(r.Value)).ToList();
        var behalf = radios.Where(r => !IsHolder(r.Value) && Label(r.Value).Contains("on behalf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (holder.Count != 1 || behalf.Count != 1) return null;

        var pick = declareAs == AsicDeclareAs.Holder ? holder[0] : behalf[0];
        var fields = boxes.ToDictionary(b => b, _ => "t");
        fields[pick.Name] = pick.Value;
        return new(boxes, pick.Value, fields);
    }

    /// <summary>Leaves a wizard without lodging: Cancel, then "Yes" on the confirmation popup.</summary>
    public async Task CancelWizardAsync(AsicPortalPage page, string source, string region, CancellationToken ct = default)
    {
        try
        {
            var cancel = FindButton(source, "Cancel") ?? (CancelIdRe().Match(source) is { Success: true } m ? m.Groups[1].Value : null);
            if (cancel is null) return;
            (page, _) = await PostEventAsync(page, cancel, process: $"{region},{cancel}", ct: ct);
            await PostEventAsync(page, "yes", process: "popup,yes", ct: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { }
    }

    public static LodgeResult ReadConfirmation(string response)
    {
        var text = PartialText(response);
        var confirmed = Regex.IsMatch(text, @"You have successfully submitted (this transaction|your request)", RegexOptions.IgnoreCase);
        var reference = ConfirmReferenceRe().Match(text) is { Success: true } m ? m.Groups[1].Value : null;
        return confirmed
            ? new LodgeResult(true, reference, null, text)
            : LodgeResult.Failed("ASIC didn't confirm the lodgement.", text);
    }

    /* ── page reading for the steps above (pure) ── */

    /// <summary>The id of the radio on the Lodgements row for <paramref name="businessName"/> (…tableBN:N:maitainBNRadio).</summary>
    public static string? FindNameRowRadio(string html, string businessName)
    {
        var want = Regex.Replace(businessName, @"\s+", " ").Trim();
        var matches = NameRowRadioRe().Matches(html).ToList();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = Math.Min(i + 1 < matches.Count ? matches[i + 1].Index : html.Length, start + 4000);
            if (PageText(html[start..end]).Contains(want, StringComparison.OrdinalIgnoreCase)) return matches[i].Groups[1].Value;
        }
        return null;
    }

    /// <summary>An ADF partial response's &lt;select&gt; options for <paramref name="selectId"/>: text → value.</summary>
    public static Dictionary<string, string> ParseOptions(string response, string selectId)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var m = Regex.Match(response, $"<select[^>]*{Regex.Escape(selectId)}::content[^>]*>(.*?)</select>", RegexOptions.Singleline);
        if (!m.Success) return options;
        foreach (Match o in OptionRe().Matches(m.Groups[1].Value))
            options.TryAdd(WebUtility.HtmlDecode(o.Groups[2].Value).Trim(), o.Groups[1].Value);
        return options;
    }

    /// <summary>The row number of the "* Email" line on the business address page.</summary>
    public static string? FindEmailRow(string html)
    {
        string? row = null;
        foreach (Match m in EmailEditRe().Matches(html))
        {
            var before = PageText(html[Math.Max(0, m.Index - 1500)..m.Index]);
            if (Regex.IsMatch(before[Math.Max(0, before.Length - 160)..], @"\*\s*Email")) row = m.Groups[1].Value;
        }
        return row;
    }

    /// <summary>The id of the button reading exactly <paramref name="label"/>.</summary>
    public static string? FindButton(string html, string label) =>
        ButtonRe().Matches(html).FirstOrDefault(m => PageText(m.Groups[2].Value).Equals(label, StringComparison.OrdinalIgnoreCase))?.Groups[1].Value;

    /// <summary>"tmp:reg:1:cb3" → "tmp": the region a wizard button is processed in.</summary>
    public static string RegionOf(string componentId) => componentId.Contains(':') ? componentId[..componentId.IndexOf(':')] : "view";

    /// <summary>A page's words: no tags, scripts or styles; whitespace squashed.</summary>
    public static string PageText(string html)
    {
        var s = ScriptRe().Replace(html, " ");
        s = TagRe().Replace(s, " ");
        return Regex.Replace(WebUtility.HtmlDecode(s), @"\s+", " ").Trim();
    }

    /// <summary>A partial response's words (its CDATA wrappers removed).</summary>
    public static string PartialText(string response) => PageText(response.Replace("<![CDATA[", " ").Replace("]]>", " "));

    [GeneratedRegex(@"id=""([^""]*tableBN:\d+:maitainBNRadio)::content""")] private static partial Regex NameRowRadioRe();
    [GeneratedRegex(@"<option[^>]*value=""([^""]*)""[^>]*>([^<]*)")] private static partial Regex OptionRe();
    [GeneratedRegex(@"<a[^>]*id=""tblAddr:(\d+):cmdLink""")] private static partial Regex EmailEditRe();
    [GeneratedRegex(@"<update id=""tblAddr"">(.*?)</update>", RegexOptions.Singleline)] private static partial Regex TableUpdateRe();
    [GeneratedRegex(@"id=""([^""]*:tableBN:(\d+):deleteLink)""")] private static partial Regex RemoveRepRe();
    [GeneratedRegex(@"<button[^>]*id=""([^""]+)""[^>]*>(.*?)</button>", RegexOptions.Singleline)] private static partial Regex ButtonRe();
    [GeneratedRegex(@"<input[^>]*name=""([^""]+)""[^>]*type=""(checkbox|radio)""[^>]*value=""([^""]+)""")] private static partial Regex DeclareInputRe();
    [GeneratedRegex(@"I am the (business name|registration or licence) holder", RegexOptions.IgnoreCase)] private static partial Regex HolderDeclRe();
    [GeneratedRegex(@"id=""([^""]*button_cancel[^""]*)""")] private static partial Regex CancelIdRe();
    [GeneratedRegex(@"Transaction reference number\s*:?\s*([0-9A-Z]+-[0-9A-Z]+)", RegexOptions.IgnoreCase)] private static partial Regex ConfirmReferenceRe();
    [GeneratedRegex(@"Holder type\s*:\s*([A-Za-z ]+?)\s+Business name holders", RegexOptions.IgnoreCase)] private static partial Regex HolderTypeRe();
    [GeneratedRegex(@"<script.*?</script>|<style.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex ScriptRe();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRe();

    /* ───────────── Holder's email (confirmed live, 7 Nov 2026: Wincare Plus Solutions) ───────────── */

    /// <summary>The residential address to put back, in ASIC's form fields (street type as ASIC's code, e.g. "Rd").</summary>
    public sealed record HolderAddress(string Unit, string StreetNumber, string StreetName, string StreetTypeCode, string Suburb, string State, string Postcode);

    /// <summary>The words of every table cell on a page, in order (the holder table: name, type, ABN, address, email, actions).</summary>
    public static IReadOnlyList<string> CellTexts(string html) =>
        CellRe().Matches(html).Select(m => PageText(m.Groups[1].Value)).Where(t => t.Length > 0).ToList();

    /// <summary>
    /// "Change holder details" → the holder's "Change Address" → today's date, Australia, the same
    /// address and our email → Next → ASIC's address check (its own spelling when it keeps our street
    /// number and postcode, else as entered, as David's script does) → Review (must show our email and
    /// street) → Submit → Declare → Submit.
    /// </summary>
    public async Task<LodgeResult> ChangeHolderEmailAsync(AsicPortalPage holderPage, HolderAddress address, string email, DateOnly effective,
        AsicDeclareAs declareAs, CancellationToken ct = default)
    {
        var changeLink = ChangeAddressLinkRe().Match(holderPage.Html) is { Success: true } link ? link.Groups[1].Value : null;
        if (changeLink is null) return LodgeResult.Failed("No \"Change Address\" link for the holder.");
        var wizard = await ClickAndFollowAsync(holderPage, changeLink, ct: ct);
        if (wizard is null) return LodgeResult.Failed("\"Change Address\" didn't open its page.");
        var html = wizard.Html;

        string? Field(string label) => FieldByLabel(html, label);
        var date = Field("Effective date (DD/MM/YYYY)");
        var country = Field("Country");
        var number = Field("Street number");
        var street = Field("Street name");
        var type = Field("Street type");
        var suburb = Field("Suburb/City");
        var postcode = Field("Postcode");
        var state = Field("State/Territory");
        var mail = Field("Email address");
        var unit = Field("Unit/Office");
        if (new[] { date, country, number, street, type, suburb, postcode, state, mail }.Any(f => f is null))
        {
            await CancelWizardAsync(wizard, html, "view", ct);
            return LodgeResult.Failed("The address form wasn't as expected.", PageText(html));
        }

        var page = wizard;
        var values = new Dictionary<string, string> { [date!] = effective.ToString("dd/MM/yyyy"), [country!] = "Australia" };
        // Choosing the country brings up the Australian address fields.
        (page, _) = await PostEventAsync(page, country!, values, ValueChangePayload, country, ct);
        values[number!] = address.StreetNumber;
        values[street!] = address.StreetName;
        values[type!] = address.StreetTypeCode;
        values[suburb!] = address.Suburb;
        values[postcode!] = address.Postcode;
        values[state!] = address.State;
        values[mail!] = email;
        if (unit is not null && address.Unit.Length > 0) values[unit] = address.Unit;

        var next = FindButton(html, "Next") ?? "button_review_1";
        var (afterNext, checkAnswer) = await PostEventAsync(page, next, values, process: $"view,{next}", ct: ct);
        page = afterNext;

        var review = checkAnswer;
        var choices = QasRadioRe().Matches(checkAnswer).Select(m => (Id: m.Groups[1].Value, Value: m.Groups[2].Value)).ToList();
        if (choices.Count > 0)
        {
            string Label(string id) => Regex.Match(checkAnswer, $"<label[^>]*for=\"{Regex.Escape(id)}\"[^>]*>(.*?)</label>", RegexOptions.Singleline) is { Success: true } l ? PageText(l.Groups[1].Value) : "";
            var typed = choices.Where(c => Regex.IsMatch(Label(c.Id), @"as typed|--\s*ent|enter as", RegexOptions.IgnoreCase)).ToList();
            var verified = choices.Where(c => !typed.Contains(c) && Label(c.Id).Contains(address.StreetNumber) && Label(c.Id).Contains(address.Postcode)).ToList();
            var pick = verified.Concat(typed).Concat(choices).First();
            values["radioQASAddressCreate"] = pick.Value;
            (page, review) = await PostEventAsync(page, "cmdConfirm", values, process: "popupSaveAddressCreate,cmdConfirm", ct: ct);
        }

        var reviewText = PartialText(review);
        if (!reviewText.Contains("Review", StringComparison.OrdinalIgnoreCase)
            || !reviewText.Contains(email, StringComparison.OrdinalIgnoreCase)
            || !reviewText.Contains(address.StreetName, StringComparison.OrdinalIgnoreCase))
        {
            await CancelWizardAsync(page, html, "view", ct);
            return LodgeResult.Failed("The review page didn't show our email and the holder's address.", reviewText);
        }

        var submit = FindButton(review, "Submit") ?? "button_validate_2";
        var (onDeclare, declare) = await PostEventAsync(page, submit, values, process: $"view,{submit}", ct: ct);
        page = onDeclare;
        var choice = ChooseDeclaration(declare, declareAs);
        if (choice is null)
        {
            await CancelWizardAsync(page, html, "view", ct);
            return LodgeResult.Failed("The Declare page wasn't as expected.", PartialText(declare));
        }
        (page, _) = await PostEventAsync(page, AsicDeclare.AuthorityField, choice.Fields, ValueChangePayload, AsicDeclare.AuthorityField, ct);
        var finish = FindButton(declare, "Submit") ?? FindButton(declare, "Next");
        if (finish is null) return LodgeResult.Failed("No Submit button on the Declare page.", PartialText(declare));
        var (_, done) = await PostEventAsync(page, finish, choice.Fields, process: $"view,{finish}", ct: ct);
        return ReadConfirmation(done);
    }

    /// <summary>The input a &lt;label&gt; with exactly these words is for (ids like "mapping_573" change between forms).</summary>
    public static string? FieldByLabel(string html, string label) =>
        FieldLabelRe().Matches(html).FirstOrDefault(m => PageText(m.Groups[2].Value).Equals(label, StringComparison.OrdinalIgnoreCase))?.Groups[1].Value;

    /// <summary>The value of the Email row on the "Change business address" page, as ASIC shows it.</summary>
    public static string? BusinessEmailShown(string html)
    {
        var row = FindEmailRow(html);
        if (row is null) return null;
        var at = html.IndexOf($"tblAddr:{row}:cmdLink", StringComparison.Ordinal);
        var text = PageText(html[Math.Max(0, html.LastIndexOf('<', at) - 1500)..html.LastIndexOf('<', at)]);
        return EmailInTextRe().Matches(text).LastOrDefault()?.Value;
    }

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)] private static partial Regex CellRe();
    [GeneratedRegex(@"id=""([^""]*tableBN:\d+:editAddressLink)""")] private static partial Regex ChangeAddressLinkRe();
    [GeneratedRegex(@"<input type=""radio"" name=""radioQASAddressCreate"" id=""([^""]+)"" value=""([^""]*)""")] private static partial Regex QasRadioRe();
    [GeneratedRegex(@"<label[^>]*for=""([^""]+?)(?:::content)?""[^>]*>(.*?)</label>", RegexOptions.Singleline)] private static partial Regex FieldLabelRe();
    [GeneratedRegex(@"[A-Za-z0-9._%+'-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")] private static partial Regex EmailInTextRe();

    public sealed record AdfForm(string Id, string Action, IReadOnlyDictionary<string, string> Fields);

    /// <summary>
    /// The form holding <paramref name="componentId"/> (or the page's ADF form), with the values a
    /// browser would send: no disabled fields, only ticked boxes, the selected option of each list.
    /// </summary>
    public static AdfForm? ReadForm(string html, string? componentId = null)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var forms = doc.QuerySelectorAll("form.af_form").ToList();
        var form = (componentId is null ? null : forms.FirstOrDefault(f => f.InnerHtml.Contains(componentId, StringComparison.Ordinal)))
                   ?? forms.FirstOrDefault();
        if (form is null) return null;
        var fields = new Dictionary<string, string>();
        foreach (var input in form.QuerySelectorAll("input[name]"))
        {
            var type = (input.GetAttribute("type") ?? "text").ToLowerInvariant();
            if (type is "submit" or "button" or "image" or "file" || input.HasAttribute("disabled")) continue;
            if (type is "checkbox" or "radio" && !input.HasAttribute("checked")) continue;
            fields[input.GetAttribute("name")!] = input.GetAttribute("value") ?? (type is "checkbox" ? "on" : "");
        }
        foreach (var select in form.QuerySelectorAll("select[name]"))
        {
            if (select.HasAttribute("disabled")) continue;
            var chosen = select.QuerySelectorAll("option").FirstOrDefault(o => o.HasAttribute("selected"))
                         ?? select.QuerySelector("option");
            fields[select.GetAttribute("name")!] = chosen?.GetAttribute("value") ?? "";
        }
        foreach (var area in form.QuerySelectorAll("textarea[name]"))
            if (!area.HasAttribute("disabled")) fields[area.GetAttribute("name")!] = area.TextContent;
        return new AdfForm(form.Id ?? "", form.GetAttribute("action") ?? "", fields);
    }

    /// <summary>The id of the link or button whose words contain <paramref name="words"/>.</summary>
    public static string? FindCommandId(string html, string words)
    {
        var doc = new HtmlParser().ParseDocument(html);
        return doc.QuerySelectorAll("a[id], button[id]")
            .FirstOrDefault(e => Regex.Replace(e.TextContent, @"\s+", " ").Contains(words, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    /// <summary>The new view state in a partial response, when it has one.</summary>
    public static string? ParsePartialViewState(string partialResponse)
    {
        var m = PartialViewStateRe().Match(partialResponse);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Where a partial response sends us (&lt;redirect url="…"/&gt;), when it does.</summary>
    public static string? ParseRedirect(string partialResponse)
    {
        var m = RedirectRe().Match(partialResponse);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    /* ───────────── Page reading (pure, unit tested) ───────────── */

    public static bool IsAuthUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Equals(AuthHost, StringComparison.OrdinalIgnoreCase);

    public sealed record LoginForm(string Action, IReadOnlyDictionary<string, string> Hidden);

    /// <summary>OAM's login form: where it posts, and its hidden fields (request_id and any others).</summary>
    public static LoginForm? ParseLoginForm(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var form = doc.QuerySelector("form#loginForm")
                   ?? doc.QuerySelectorAll("form").FirstOrDefault(f => f.QuerySelector("input[name='password']") is not null);
        if (form is null) return null;
        var hidden = form.QuerySelectorAll("input[type='hidden']")
            .Where(i => !string.IsNullOrEmpty(i.GetAttribute("name")))
            .GroupBy(i => i.GetAttribute("name")!)
            .ToDictionary(g => g.Key, g => g.First().GetAttribute("value") ?? "");
        var action = form.GetAttribute("action");
        return new LoginForm(string.IsNullOrWhiteSpace(action) ? "/oam/server/auth_cred_submit" : action, hidden);
    }

    /// <summary>Why OAM sent us back to the login page, in words.</summary>
    public static string LoginError(string url, string html)
    {
        var code = Regex.Match(url, @"[?&]p_error_code=([^&]+)").Groups[1].Value;
        code = Uri.UnescapeDataString(code);
        var meaning = code switch
        {
            "OAM-1" or "OAM-2" => "the username or password is wrong",
            "OAM-5" or "OAM-10" => "the account is locked; unlock it at ASIC before trying again",
            "OAM-8" or "OAM-9" => "the login failed; try again in a few minutes",
            "" => null,
            _ => null,
        };
        if (meaning is not null) return $"ASIC Connect refused the login: {meaning} ({code}).";

        // Fall back to whatever error text the page shows.
        var doc = new HtmlParser().ParseDocument(html);
        var text = doc.QuerySelectorAll(".loginFailed, .error, .errorText, #errorMsg, .message")
            .Select(e => Regex.Replace(e.TextContent, @"\s+", " ").Trim())
            .FirstOrDefault(t => t.Length > 0);
        return "ASIC Connect refused the login" + (text is not null ? $": {text}" : code.Length > 0 ? $" ({code})." : ".");
    }

    public sealed record Loopback(string Loop, string WindowId, string SessionSuffix);

    /// <summary>The values in ADF's AdfLoopbackUtils.runLoopback(...) call, when the page is one.</summary>
    public static Loopback? ParseLoopback(string html)
    {
        var loop = LoopRe().Match(html);
        var window = WindowRe().Match(html);
        if (!loop.Success || !window.Success) return null;
        var session = SessionRe().Match(html);
        return new Loopback(loop.Groups[1].Value, window.Groups[1].Value, session.Success ? session.Groups[1].Value : "");
    }

    [GeneratedRegex(@"'_afrLoop',\s*'(\d+)'")] private static partial Regex LoopRe();
    [GeneratedRegex(@"'_afrPage',\s*'',\s*'(\w+)'")] private static partial Regex WindowRe();
    [GeneratedRegex(@"'(;jsessionid=[^']+)'")] private static partial Regex SessionRe();
    [GeneratedRegex(@"<redirect\s+url=""([^""]+)""")] private static partial Regex RedirectRe();
    [GeneratedRegex(@"id=""javax\.faces\.ViewState""><!\[CDATA\[([^\]]+)\]\]>")] private static partial Regex PartialViewStateRe();
}
