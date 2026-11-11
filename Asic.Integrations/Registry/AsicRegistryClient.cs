#nullable enable
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Asic.Integrations.Shared;

namespace Asic.Integrations.Registry;

/// <summary>
/// Queries the ASIC business-names registry. Preserves the original ADF/JSF request
/// sequence and DOM parsing (the hard-won part), refactored for DI, options, an
/// injectable captcha solver, and an isolated per-instance cookie session.
/// </summary>
public sealed class AsicRegistryClient : IAsicRegistryClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly ICaptchaSolver _captcha;
    private readonly AsicRegistryOptions _options;
    private readonly HtmlParser _htmlParser = new();

    // Session state established per search.
    private string? _adfWindowId;
    private string? _viewState;

    public AsicRegistryClient(AsicRegistryOptions options, ICaptchaSolver captcha)
        : this(options, captcha, handler: null)
    {
    }

    /// <summary>Tests: a scripted <paramref name="handler"/> in place of ASIC.</summary>
    internal AsicRegistryClient(AsicRegistryOptions options, ICaptchaSolver captcha, HttpMessageHandler? handler)
    {
        _options = options;
        _captcha = captcha;

        // Its own cookie jar so a run's ASIC session is isolated (the scraper relies on session
        // cookies + ADF window ids), with the TLS settings Cloudflare requires and the optional proxy.
        handler ??= AsicHttp.CreateHandler(new AsicHttpSettings
        {
            Decompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ForceTls13 = _options.ForceTls13,
            ProxyUrl = _options.ProxyUrl,
            ProxyUsername = _options.ProxyUsername,
            ProxyPassword = _options.ProxyPassword,
        });
        _http = AsicHttp.CreateClient(handler, _options.BaseUrl, TimeSpan.FromSeconds(_options.TimeoutSeconds), _options.UserAgent);
    }

    public async Task<AsicRegistrySearchResult> SearchByAbnAndNameAsync(string abn, string businessName, CancellationToken cancellationToken)
    {
        var document = await GetSearchDocumentAsync(abn, cancellationToken);
        if (document is null)
        {
            return AsicRegistrySearchResult.Fail("The ASIC search page could not be loaded.");
        }

        var content = document.TextContent;
        var hasMultiple = false;

        if (content.Contains("Business names search results"))
        {
            var id = document.QuerySelectorAll("a[id*='bnConnectionTemplate:r1:']")
                .FirstOrDefault(x => (x.GetAttribute("id")?.Contains("orgName") ?? false) && x.TextContent.Contains(businessName))?
                .GetAttribute("id")?
                .Replace(":orgName", "");

            document = await GetBusinessNameDocumentAsync(id, 0, useRichMessage: true, cancellationToken);
            hasMultiple = true;
        }

        var table = document.QuerySelector(".detailTable[id*='bnConnectionTemplate:r1']");
        var parsed = ParseBusinessName(table);

        return parsed is null
            ? AsicRegistrySearchResult.Fail("No matching business name was found on ASIC for this name.")
            : AsicRegistrySearchResult.Ok([parsed], hasMultiple);
    }

    public async Task<AsicRegistrySearchResult> SearchByAbnAsync(string abn, CancellationToken cancellationToken)
    {
        var document = await GetSearchDocumentAsync(abn, cancellationToken);
        if (document is null)
        {
            return AsicRegistrySearchResult.Fail("The ASIC search page could not be loaded.");
        }

        var content = document.Source.Text;
        var businessNames = new List<AsicRegistryBusinessName>();

        if (content.Contains("Business names search results"))
        {
            var hasMorePages = true;

            while (hasMorePages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var searchRegionIndex = GetRegionIndexFromDocument(document);

                var resultCount = document.QuerySelectorAll("a[id*='bnConnectionTemplate:r1:']")
                    .Count(x => x.GetAttribute("id")?.Contains("orgName") ?? false);

                for (var resultIndex = 0; resultIndex < resultCount; resultIndex++)
                {
                    var id = $"bnConnectionTemplate:r1:{searchRegionIndex}:t1:{resultIndex}";

                    document = await GetBusinessNameDocumentAsync(id, searchRegionIndex, useRichMessage: true, cancellationToken);

                    var detailRegionIndex = GetRegionIndexFromDocument(document);

                    var table = document.QuerySelector(".detailTable[id*='bnConnectionTemplate:r1']");
                    var parsed = ParseBusinessName(table);

                    if (parsed is not null)
                    {
                        businessNames.Add(parsed);
                    }

                    if (resultIndex != resultCount - 1)
                    {
                        document = await GoBackToSearchResultsAsync(detailRegionIndex, cancellationToken);
                    }
                }

                var detailRegion = GetRegionIndexFromDocument(document);
                var nextButtonEnabled = document.QuerySelector("a[id*='next']")?.GetAttribute("class")?.Contains("p_AFDisabled") == false;

                if (nextButtonEnabled)
                {
                    document = await GoBackToSearchResultsAsync(detailRegion, cancellationToken);
                    var currentRegion = GetRegionIndexFromDocument(document);
                    document = await NavigateToNextPageAsync(currentRegion, cancellationToken);
                }
                else
                {
                    hasMorePages = false;
                }
            }
        }
        else
        {
            var table = document.QuerySelector(".detailTable[id*='bnConnectionTemplate:r1']");
            var parsed = ParseBusinessName(table);
            if (parsed is not null)
            {
                businessNames.Add(parsed);
            }
        }

        return AsicRegistrySearchResult.Ok(businessNames, businessNames.Count > 1);
    }

    private async Task<IHtmlDocument?> GetSearchDocumentAsync(string abn, CancellationToken cancellationToken)
    {
        var url = $"RegistrySearch/faces/landing/panelSearch.jspx?searchType=Bn&searchName=&searchNumber={abn}";

        var content = await _http.GetStringAsync(url, cancellationToken);

        content = await _http.GetStringAsync(
            $"RegistrySearch/faces/landing/panelSearch.jspx?searchType=Bn&searchName=&searchNumber={abn}&{Adf.LoopbackQuery(Adf.LoopId(content), Adf.ScriptWindowId(content), windowHeight: 924)}",
            cancellationToken);

        var document = await _htmlParser.ParseDocumentAsync(content, cancellationToken);

        if (!content.Contains("Business names search results") && !content.Contains("Business Name Summary"))
        {
            (_adfWindowId, _viewState) = Adf.HiddenFields(document);

            if (!_captcha.IsConfigured)
            {
                throw new InvalidOperationException(
                    "ASIC presented a CAPTCHA but no 2Captcha API key is configured. Set AsicRegistry:TwoCaptchaApiKey to read renewal dates.");
            }

            var (solved, captchaError) = await SolveCaptchaAsync(_adfWindowId, _viewState, cancellationToken);

            if (string.IsNullOrEmpty(solved))
            {
                throw new InvalidOperationException(
                    $"The ASIC CAPTCHA could not be solved. {captchaError ?? "2Captcha returned no result or timed out."}");
            }

            content = solved;
            document = await _htmlParser.ParseDocumentAsync(content, cancellationToken);
        }

        var (windowId, viewState) = Adf.HiddenFields(document);
        _adfWindowId = windowId ?? _adfWindowId;
        _viewState = viewState ?? _viewState;

        return document;
    }

    private async Task<IHtmlDocument> GetBusinessNameDocumentAsync(string? id, int regionIndex, bool useRichMessage, CancellationToken cancellationToken)
    {
        var url = $"RegistrySearch/faces/landing/panelSearch.jspx?Adf-Window-Id={_adfWindowId}&Adf-Page-Id=1";
        var body = $"bnConnectionTemplate:pt_s5:templateSearchTypesListOfValuesId=6&bnConnectionTemplate:pt_s5:searchSurname=&bnConnectionTemplate:pt_s5:searchFirstName=&bnConnectionTemplate:pt_s5:templateSearchInputText=&bnConnectionTemplate:pt_s5:searchName=Name&bnConnectionTemplate:pt_s5:searchNumber=Number&bnConnectionTemplate:r1:{regionIndex}:totalItemsSelected=0&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchTypesLovId=1&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchSurname=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchFirstName=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchForTextId=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchForName=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchForNumber=&bnConnectionTemplate:r1:{regionIndex}:fetchsize=0&bnConnectionTemplate:r1:{regionIndex}:fetchsizetwin=0&org.apache.myfaces.trinidad.faces.FORM=f1&Adf-Window-Id={_adfWindowId}&javax.faces.ViewState={_viewState}&Adf-Page-Id=1&oracle.adf.view.rich.RENDER=bnConnectionTemplate%3Ar1&oracle.adf.view.rich.DELTAS=%7BbnConnectionTemplate%3Ar1%3A{regionIndex}%3At1%3D%7Brows%3D10%7D%7D&event={id}%3AorgName&event.{id}:orgName={Adf.ActionEvent}&oracle.adf.view.rich.PROCESS=bnConnectionTemplate%3Ar1%2C{id}%3AorgName";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        if (useRichMessage)
        {
            request.Headers.TryAddWithoutValidation("Adf-Rich-Message", "true");
        }

        var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (useRichMessage)
        {
            return await ParseRichMessageResponseAsync(content, cancellationToken);
        }

        var document = await _htmlParser.ParseDocumentAsync(content, cancellationToken);
        _viewState = document.QuerySelector("input[name='javax.faces.ViewState']")?.GetAttribute("value") ?? _viewState;
        return document;
    }

    private async Task<IHtmlDocument> GoBackToSearchResultsAsync(int regionIndex, CancellationToken cancellationToken)
    {
        var url = $"RegistrySearch/faces/landing/panelSearch.jspx?Adf-Window-Id={_adfWindowId}&Adf-Page-Id=1";
        var body = $"bnConnectionTemplate:pt_s5:templateSearchTypesListOfValuesId=6&bnConnectionTemplate:pt_s5:searchSurname=&bnConnectionTemplate:pt_s5:searchFirstName=&bnConnectionTemplate:pt_s5:templateSearchInputText=&bnConnectionTemplate:pt_s5:searchName=Name&bnConnectionTemplate:pt_s5:searchNumber=Number&org.apache.myfaces.trinidad.faces.FORM=f1&Adf-Window-Id={_adfWindowId}&javax.faces.ViewState={_viewState}&Adf-Page-Id=1&oracle.adf.view.rich.RENDER=bnConnectionTemplate%3Ar1&oracle.adf.view.rich.DELTAS=%7BbnConnectionTemplate%3Ar1%3A{regionIndex}%3At1%3D%7Brows%3D10%7D%7D&event=bnConnectionTemplate%3Ar1%3A{regionIndex}%3Acb7&event.bnConnectionTemplate:r1:{regionIndex}:cb7={Adf.ActionEvent}&oracle.adf.view.rich.PROCESS=bnConnectionTemplate%3Ar1%2CbnConnectionTemplate%3Ar1%3A{regionIndex}%3Acb7";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.TryAddWithoutValidation("Adf-Rich-Message", "true");

        var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return await ParseRichMessageResponseAsync(content, cancellationToken);
    }

    private async Task<IHtmlDocument> NavigateToNextPageAsync(int regionIndex, CancellationToken cancellationToken)
    {
        var url = $"RegistrySearch/faces/landing/panelSearch.jspx?Adf-Window-Id={_adfWindowId}&Adf-Page-Id=1";
        var body = $"bnConnectionTemplate:pt_s5:templateSearchTypesListOfValuesId=6&bnConnectionTemplate:pt_s5:searchSurname=&bnConnectionTemplate:pt_s5:searchFirstName=&bnConnectionTemplate:pt_s5:templateSearchInputText=&bnConnectionTemplate:pt_s5:searchName=Name&bnConnectionTemplate:pt_s5:searchNumber=Number&bnConnectionTemplate:r1:{regionIndex}:totalItemsSelected=0&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchTypesLovId=1&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchSurname=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchFirstName=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchForTextId=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchForName=&bnConnectionTemplate:r1:{regionIndex}:generalSearchPanelFragment:s4:searchForNumber=&bnConnectionTemplate:r1:{regionIndex}:fetchsize=0&bnConnectionTemplate:r1:{regionIndex}:fetchsizetwin=0&org.apache.myfaces.trinidad.faces.FORM=f1&Adf-Window-Id={_adfWindowId}&javax.faces.ViewState={_viewState}&Adf-Page-Id=1&oracle.adf.view.rich.RENDER=bnConnectionTemplate%3Ar1&oracle.adf.view.rich.DELTAS=%7BbnConnectionTemplate%3Ar1%3A{regionIndex}%3At1%3D%7Brows%3D10%7D%7D&event=bnConnectionTemplate%3Ar1%3A{regionIndex}%3At1%3Anext&event.bnConnectionTemplate:r1:{regionIndex}:t1:next={Adf.ActionEvent}&oracle.adf.view.rich.PROCESS=bnConnectionTemplate%3Ar1%2CbnConnectionTemplate%3Ar1%3A{regionIndex}%3At1%3Anext";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.TryAddWithoutValidation("Adf-Rich-Message", "true");

        var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return await ParseRichMessageResponseAsync(content, cancellationToken);
    }

    // Extract the HTML region + ViewState from an ADF Rich-Message XML response.
    private async Task<IHtmlDocument> ParseRichMessageResponseAsync(string xmlContent, CancellationToken cancellationToken)
    {
        var viewStateMatch = Regex.Match(xmlContent, @"<update id=""javax\.faces\.ViewState""><!\[CDATA\[(.+?)\]\]></update>");
        if (viewStateMatch.Success)
        {
            _viewState = viewStateMatch.Groups[1].Value;
        }

        var htmlMatch = Regex.Match(xmlContent, @"<update id=""bnConnectionTemplate:r1""><!\[CDATA\[(.+?)\]\]></update>", RegexOptions.Singleline);
        if (htmlMatch.Success)
        {
            return await _htmlParser.ParseDocumentAsync(htmlMatch.Groups[1].Value, cancellationToken);
        }

        return await _htmlParser.ParseDocumentAsync(xmlContent, cancellationToken);
    }

    private int GetRegionIndexFromDocument(IHtmlDocument document)
    {
        var element = document.QuerySelector("[id^='bnConnectionTemplate:r1:']");
        var id = element?.GetAttribute("id");
        if (id is not null)
        {
            var match = Regex.Match(id, @"bnConnectionTemplate:r1:(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
            {
                return index;
            }
        }
        return 0;
    }

    private static AsicRegistryBusinessName? ParseBusinessName(IElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var holdersDetails = element.QuerySelectorAll("th")
            .FirstOrDefault(x => x.TextContent.Contains("Holder(s) details:"))?
            .NextElementSibling?.QuerySelectorAll("span");

        var holders = new List<AsicHolder>();

        if (holdersDetails is not null)
        {
            var holderNames = holdersDetails.Where(x => x.TextContent.Contains("Holder Name:"))
                .Select(x => x.NextElementSibling?.TextContent).ToArray();
            var holderTypes = holdersDetails.Where(x => x.TextContent.Contains("Holder Type:"))
                .Select(x => x.NextSibling?.TextContent).ToArray();
            var holderAbns = holdersDetails.Where(x => x.TextContent.Contains("ABN:"))
                .Select(x => x.ParentElement?.NextElementSibling?.TextContent).ToArray();

            for (var i = 0; i < holderNames.Length; i++)
            {
                holders.Add(new AsicHolder(
                    holderNames[i],
                    i < holderTypes.Length ? holderTypes[i] : null,
                    i < holderAbns.Length ? holderAbns[i]?.Replace("(External Link)", "").Trim() : null));
            }
        }

        string? DetailFor(string label) => element.QuerySelectorAll("th")
            .FirstOrDefault(x => x.TextContent.Contains(label))?.NextElementSibling?.TextContent;

        return new AsicRegistryBusinessName(
            Name: DetailFor("Business name:"),
            Status: DetailFor("Status:"),
            RegistrationDate: DetailFor("Registration date:"),
            RenewalDate: DetailFor("Renewal date:"),
            CancelledDate: DetailFor("Cancelled date:"),
            CancellationUnderReview: DetailFor("Cancellation under review:"),
            AddressForServiceDocuments: DetailFor("Address for service of documents:"),
            PrincipalPlaceOfBusiness: DetailFor("Principal place of business:"),
            Holders: holders);
    }

    private async Task<(string? Content, string? Error)> SolveCaptchaAsync(string? adfWindowId, string? viewState, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_options.BaseUrl}RegistrySearch/faces/landing/panelSearch.jspx?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0";

            var solve = await _captcha.SolveReCaptchaAsync(
                _options.RecaptchaSiteKey, url, action: "userverify", invisible: true, enterprise: false, cancellationToken);

            if (!solve.Succeeded)
            {
                return (null, solve.Error);
            }

            var token = solve.Token;

            var body = $"bnConnectionTemplate:pt_s5:templateSearchTypesListOfValuesId=6&bnConnectionTemplate:pt_s5:searchSurname=&bnConnectionTemplate:pt_s5:searchFirstName=&bnConnectionTemplate:pt_s5:templateSearchInputText=&bnConnectionTemplate:pt_s5:searchName=Name&bnConnectionTemplate:pt_s5:searchNumber=Number&g-recaptcha-response={token}&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchTypesLovId=0&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchSurname=&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchFirstName=&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchForTextId=Name+or+Number&org.apache.myfaces.trinidad.faces.FORM=f1&Adf-Window-Id={adfWindowId}&Adf-Page-Id=0&javax.faces.ViewState={viewState}&event=bnConnectionTemplate%3Ar1%3A0%3AsearchButtonCap&event.bnConnectionTemplate:r1:0:searchButtonCap={Adf.ActionEvent}&oracle.adf.view.rich.PROCESS=bnConnectionTemplate%3Ar1%2CbnConnectionTemplate%3Ar1%3A0%3AsearchButtonCap";

            var response = await _http.PostAsync(url,
                new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"), cancellationToken);

            return (await response.Content.ReadAsStringAsync(cancellationToken), null);
        }
        catch (Exception ex)
        {
            return (null, $"CAPTCHA submission to ASIC failed: {ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}
