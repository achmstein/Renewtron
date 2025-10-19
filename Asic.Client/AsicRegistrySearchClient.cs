using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Captcha;
using Asic.Client.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Asic.Client;

public class AsicRegistrySearchClient : IAsicRegistrySearchClient
{
    private readonly HttpClient _http;
    private readonly HtmlParser _htmlParser;
    private readonly ICaptchaSolver _captchaSolver;

    public AsicRegistrySearchClient(ICaptchaSolver captchaSolver)
    {
        _captchaSolver = captchaSolver ?? throw new ArgumentNullException(nameof(captchaSolver));

        _http = new HttpClient()
        {
            BaseAddress = new Uri("https://connectonline.asic.gov.au/")
        };

        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");

        _htmlParser = new HtmlParser();
    }

    // Single business name search
    public async Task<SearchResult<BusinessNameResponse>> SearchAsync(string abn, string name)
    {
        var documentResult = await GetSearchDocumentAsync(abn);

        if (documentResult.RequiresCaptcha)
        {
            // Solve captcha internally
            var captchaToken = await _captchaSolver.SolveAsync(documentResult.CaptchaChallenge);
            documentResult = await GetSearchDocumentWithCaptchaAsync(documentResult.CaptchaChallenge, captchaToken);

            if (!documentResult.Success || documentResult.Data is null)
            {
                return SearchResult<BusinessNameResponse>.Failed();
            }
        }

        if (!documentResult.Success || documentResult.Data is null)
        {
            return SearchResult<BusinessNameResponse>.Failed();
        }

        var document = documentResult.Data;
        var content = document.TextContent;
        bool hasMultipleBusinesses = false;

        if (content.Contains("Business names search results"))
        {
            var adfWindowId = document.QuerySelector("input[name='Adf-Window-Id']").GetAttribute("value");
            var viewState = document.QuerySelector("input[name='javax.faces.ViewState']").GetAttribute("value");

            var id = document.QuerySelectorAll("a[id*='bnConnectionTemplate:r1:']")
                .FirstOrDefault(x => x.GetAttribute("id").Contains("orgName") && x.TextContent.Contains(name))?
                .GetAttribute("id")
                .Replace(":orgName", "");

            document = await GetBusinessNameDocumentAsync(id, adfWindowId, viewState);
            hasMultipleBusinesses = true;
        }

        var table = document.QuerySelector(".detailTable[id*='bnConnectionTemplate:r1']");
        var businessName = GetBusinessNameFromElement(table);

        var response = businessName is null
            ? BusinessNameResponse.Failure()
            : BusinessNameResponse.Success(businessName, hasMultipleBusinesses);

        return SearchResult<BusinessNameResponse>.SuccessResult(response);
    }

    // Multiple business names search
    public async Task<SearchResult<BusinessNamesResponse>> SearchAsync(string abn)
    {
        var documentResult = await GetSearchDocumentAsync(abn);

        if (documentResult.RequiresCaptcha)
        {
            // Solve captcha internally
            var captchaToken = await _captchaSolver.SolveAsync(documentResult.CaptchaChallenge);
            documentResult = await GetSearchDocumentWithCaptchaAsync(documentResult.CaptchaChallenge, captchaToken);

            if (!documentResult.Success || documentResult.Data is null)
            {
                return SearchResult<BusinessNamesResponse>.Failed();
            }
        }

        if (!documentResult.Success || documentResult.Data is null)
        {
            return SearchResult<BusinessNamesResponse>.Failed();
        }

        var document = documentResult.Data;
        var content = document.Source.Text;
        List<BusinessName> businessNames = [];

        if (content.Contains("Business names search results"))
        {
            var ids = document.QuerySelectorAll("a[id*='bnConnectionTemplate:r1:']")
                .Where(x => x.GetAttribute("id").Contains("orgName"))
                .Select(x => x.GetAttribute("id").Replace(":orgName", ""))
                .ToList();

            foreach (var id in ids)
            {
                var adfWindowId = document.QuerySelector("input[name='Adf-Window-Id']").GetAttribute("value");
                var viewState = document.QuerySelector("input[name='javax.faces.ViewState']").GetAttribute("value");

                document = await GetBusinessNameDocumentAsync(id, adfWindowId, viewState);
                var table = document.QuerySelector(".detailTable[id*='bnConnectionTemplate:r1']");
                var businessName = GetBusinessNameFromElement(table);

                if (businessName is not null)
                {
                    businessNames.Add(businessName);
                }

                if (ids.IndexOf(id) != ids.Count - 1)
                {
                    var nextDocResult = await GetSearchDocumentAsync(abn);
                    if (nextDocResult.Success && nextDocResult.Data is not null)
                    {
                        document = nextDocResult.Data;
                    }
                }
            }
        }
        else
        {
            var table = document.QuerySelector(".detailTable[id*='bnConnectionTemplate:r1']");
            var businessName = GetBusinessNameFromElement(table);

            if (businessName is not null)
            {
                businessNames.Add(businessName);
            }
        }

        return SearchResult<BusinessNamesResponse>.SuccessResult(BusinessNamesResponse.Success(businessNames));
    }

    async Task<SearchResult<IHtmlDocument>> GetSearchDocumentAsync(string abn)
    {
        var url = $"RegistrySearch/faces/landing/panelSearch.jspx?searchType=Bn&searchName=&searchNumber={abn}";

        var content = await _http.GetStringAsync(url);

        var afrLoop = Regex.Match(content, @"_afrLoop',\n '(?<AfrLoop>\d+)'").Groups["AfrLoop"].Value;
        var afrPage = Regex.Match(content, @"_afrPage',\n '',\n '(?<AfrPage>\w+)'").Groups["AfrPage"].Value;

        content = await _http.GetStringAsync($"RegistrySearch/faces/landing/panelSearch.jspx?searchType=Bn&searchName=&searchNumber={abn}&_afrLoop={afrLoop}&_afrWindowMode=2&Adf-Window-Id={afrPage}&_afrFS=16&_afrMT=screen&_afrMFW=1865&_afrMFH=924&_afrMFDW=1920&_afrMFDH=1080&_afrMFC=8&_afrMFCI=0&_afrMFM=0&_afrMFR=96&_afrMFG=0&_afrMFS=0&_afrMFO=0");

        var document = await _htmlParser.ParseDocumentAsync(content);

        if (!content.Contains("Business names search results") && !content.Contains("Business Name Summary"))
        {
            var adfWindowId = document.QuerySelector("input[name='Adf-Window-Id']")?.GetAttribute("value");
            var viewState = document.QuerySelector("input[name='javax.faces.ViewState']")?.GetAttribute("value");

            if (string.IsNullOrEmpty(adfWindowId) || string.IsNullOrEmpty(viewState))
            {
                return SearchResult<IHtmlDocument>.Failed();
            }

            var challenge = new CaptchaChallenge
            {
                AdfWindowId = adfWindowId,
                ViewState = viewState,
                CaptchaUrl = $"https://connectonline.asic.gov.au/RegistrySearch/faces/landing/panelSearch.jspx?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0"
            };

            return SearchResult<IHtmlDocument>.CaptchaRequired(challenge);
        }

        return SearchResult<IHtmlDocument>.SuccessResult(document);
    }

    async Task<SearchResult<IHtmlDocument>> GetSearchDocumentWithCaptchaAsync(
        CaptchaChallenge challenge,
        string captchaToken)
    {
        try
        {
            var response = await _http.PostAsync(challenge.CaptchaUrl,
                new StringContent($"bnConnectionTemplate:pt_s5:templateSearchTypesListOfValuesId=6&bnConnectionTemplate:pt_s5:searchSurname=&bnConnectionTemplate:pt_s5:searchFirstName=&bnConnectionTemplate:pt_s5:templateSearchInputText=&bnConnectionTemplate:pt_s5:searchName=Name&bnConnectionTemplate:pt_s5:searchNumber=Number&g-recaptcha-response={captchaToken}&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchTypesLovId=0&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchSurname=&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchFirstName=&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchForTextId=Name+or+Number&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchForName=&bnConnectionTemplate:r1:0:searchPanelLanding:dc1:s1:searchForNumber=&org.apache.myfaces.trinidad.faces.FORM=f1&Adf-Window-Id={challenge.AdfWindowId}&Adf-Page-Id=0&javax.faces.ViewState={challenge.ViewState}&event=bnConnectionTemplate%3Ar1%3A0%3AsearchButtonCap&event.bnConnectionTemplate:r1:0:searchButtonCap=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&oracle.adf.view.rich.PROCESS=bnConnectionTemplate%3Ar1%2CbnConnectionTemplate%3Ar1%3A0%3AsearchButtonCap", Encoding.UTF8, "application/x-www-form-urlencoded"));

            var content = await response.Content.ReadAsStringAsync();
            var document = await _htmlParser.ParseDocumentAsync(content);

            return SearchResult<IHtmlDocument>.SuccessResult(document);
        }
        catch
        {
            return SearchResult<IHtmlDocument>.Failed();
        }
    }

    async Task<IHtmlDocument> GetBusinessNameDocumentAsync(string id, string adfWindowId, string viewState)
    {
        var response = await _http.PostAsync($"RegistrySearch/faces/landing/panelSearch.jspx?Adf-Window-Id={adfWindowId}&Adf-Page-Id=1",
            new StringContent($"bnConnectionTemplate:pt_s5:templateSearchTypesListOfValuesId=6&bnConnectionTemplate:pt_s5:searchSurname=&bnConnectionTemplate:pt_s5:searchFirstName=&bnConnectionTemplate:pt_s5:templateSearchInputText=&bnConnectionTemplate:pt_s5:searchName=Name&bnConnectionTemplate:pt_s5:searchNumber=Number&bnConnectionTemplate:r1:0:totalItemsSelected=0&bnConnectionTemplate:r1:0:generalSearchPanelFragment:s4:searchTypesLovId=1&bnConnectionTemplate:r1:0:generalSearchPanelFragment:s4:searchSurname=&bnConnectionTemplate:r1:0:generalSearchPanelFragment:s4:searchFirstName=&bnConnectionTemplate:r1:0:generalSearchPanelFragment:s4:searchForTextId=&bnConnectionTemplate:r1:0:generalSearchPanelFragment:s4:searchForName=&bnConnectionTemplate:r1:0:generalSearchPanelFragment:s4:searchForNumber=&bnConnectionTemplate:r1:0:fetchsize=0&bnConnectionTemplate:r1:0:fetchsizetwin=0&org.apache.myfaces.trinidad.faces.FORM=f1&Adf-Window-Id={adfWindowId}&javax.faces.ViewState={viewState}&Adf-Page-Id=1&oracle.adf.view.rich.RENDER=bnConnectionTemplate%3Ar1&oracle.adf.view.rich.DELTAS=%7BbnConnectionTemplate%3Ar1%3A0%3At1%3D%7Brows%3D2%7D%7D&event={id}%3AorgName&event.{id}:orgName=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&oracle.adf.view.rich.PROCESS=bnConnectionTemplate%3Ar1%2C{id}%3AorgName", Encoding.UTF8, "application/x-www-form-urlencoded"));

        var content = await response.Content.ReadAsStringAsync();
        var document = await _htmlParser.ParseDocumentAsync(content);

        return document;
    }

    BusinessName GetBusinessNameFromElement(IElement element)
    {
        if (element is null) return null;

        var holdersDetails = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Holder(s) details:"))?.NextElementSibling.QuerySelectorAll("span");

        var holderNames = holdersDetails?.Where(x => x.TextContent.Contains("Holder Name:")).Select(x => x.NextElementSibling.TextContent).ToArray() ?? [];
        var holderTypes = holdersDetails?.Where(x => x.TextContent.Contains("Holder Type:")).Select(x => x.NextSibling.TextContent).ToArray() ?? [];
        var holderAbns = holdersDetails?.Where(x => x.TextContent.Contains("ABN:")).Select(x => x.ParentElement.NextElementSibling.TextContent).ToArray() ?? [];

        return new()
        {
            Name = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Business name:"))?.NextElementSibling.TextContent,
            Status = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Status:"))?.NextElementSibling.TextContent,
            RegistrationDate = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Registration date:"))?.NextElementSibling.TextContent,
            RenewalDate = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Renewal date:"))?.NextElementSibling.TextContent,
            CancelledDate = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Cancelled date:"))?.NextElementSibling.TextContent,
            CancellationUnderReview = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Cancellation under review:"))?.NextElementSibling.TextContent,
            AddressForServiceDocuments = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Address for service of documents:"))?.NextElementSibling.TextContent,
            PrincipalPlaceOfBusiness = element.QuerySelectorAll("th").FirstOrDefault(x => x.TextContent.Contains("Principal place of business:"))?.NextElementSibling.TextContent,
            Holders = [.. holderNames.Select((x, i) => new Holder
            {
                Name = x,
                Type = holderTypes[i],
                Abn = holderAbns[i].Replace("(External Link)", "").Trim()
            })],
        };
    }
}
