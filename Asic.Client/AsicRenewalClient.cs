using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Asic.Client;

public class AsicRenewalClient : IAsicRenewalClient
{
    private readonly HttpClient _http;
    private readonly HttpClientHandler _httpHandler;
    private readonly HtmlParser _htmlParser;
    private readonly IAsicPaymentClient _paymentClient;

    public AsicRenewalClient(IAsicPaymentClient paymentClient)
    {
        _httpHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer()
        };

        _http = new HttpClient(_httpHandler)
        {
            BaseAddress = new Uri("https://asicconnect.asic.gov.au/"),
            Timeout = TimeSpan.FromSeconds(300),
        };

        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");

        _htmlParser = new HtmlParser();
        _paymentClient = paymentClient;
    }

    /// <summary>
    /// Clears all cookies and resets the session state.
    /// Call this before starting a new search to ensure a fresh session.
    /// </summary>
    private void ResetSession()
    {
        _httpHandler.CookieContainer = new System.Net.CookieContainer();
    }

    public async Task<BusinessNamesResult> SearchByAbnAsync(string abn)
    {
        try
        {
            // Reset session to ensure fresh state for each search
            ResetSession();

            // Step 1: Initialize renewal session
            var (sessionId, adfWindowId, viewState) = await InitializeSessionAsync();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(adfWindowId) || string.IsNullOrEmpty(viewState))
            {
                return BusinessNamesResult.Failed("Failed to initialize renewal session");
            }

            // Step 2: Submit ABN to get business name list
            var (success, content) = await SubmitAbnForSearchAsync(abn, adfWindowId, viewState);
            if (!success)
            {
                var errorMessage = GetUserFriendlyErrorMessage(content);
                return BusinessNamesResult.Failed(errorMessage ?? "Failed to search for business names. Please check the ABN and try again.");
            }

            // Step 3: Parse the response to extract business names
            var businessNames = ParseBusinessNamesFromResponse(content);

            if (businessNames.Count == 0)
            {
                return BusinessNamesResult.Failed("No business names found for this ABN");
            }

            return BusinessNamesResult.Succeeded(businessNames);
        }
        catch (Exception ex)
        {
            return BusinessNamesResult.Failed($"Search failed: {ex.Message}");
        }
    }

    public async Task<RenewalResult> RenewBusinessNameAsync(string abn, string accountNumber, int renewalYears, string email, CreditCardDetails cardDetails)
    {
        try
        {
            // Reset session to ensure fresh state for each renewal
            ResetSession();

            var initialData = RenewalStepData.Create(abn, accountNumber, renewalYears, email, cardDetails);

            // Railway-oriented pipeline: each step only executes if the previous step succeeded
            return await InitializeSessionStepAsync(initialData)
                .ThenAsync("Submit ABN", SubmitAbnStepAsync)
                .ThenAsync("Select Business", SelectBusinessStepAsync)
                .ThenAsync("Proceed to Renewal Period", ProceedToRenewalPeriodStepAsync)
                .ThenAsync("Select Renewal Period", SelectRenewalPeriodStepAsync)
                .ThenAsync("Proceed to Review", ProceedToReviewStepAsync)
                .ThenAsync("Select Authority Declaration", SelectAuthorityDeclarationStepAsync)
                .ThenAsync("Proceed to Payment", ProceedToPaymentStepAsync)
                .ThenAsync("Submit Email", SubmitEmailStepAsync)
                .ThenAsync("Select Payment Method", SelectPaymentMethodStepAsync)
                .ThenAsync("Open Payment Gateway", OpenPaymentGatewayStepAsync)
                .ThenAsync("Process Payment", ProcessPaymentStepAsync)
                .ToRenewalResultAsync(data =>
                    RenewalResult.Success(
                        data.TransactionReference,
                        data.HostedTokenizationId));
        }
        catch (Exception ex)
        {
            return RenewalResult.Failed($"Renewal failed: {ex.Message}", "Exception");
        }
    }


    private async Task<StepResult<RenewalStepData>> InitializeSessionStepAsync(RenewalStepData data)
    {
        var (sessionId, adfWindowId, viewState) = await InitializeSessionAsync();

        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(adfWindowId) || string.IsNullOrEmpty(viewState))
        {
            return StepResult<RenewalStepData>.Failure("Failed to initialize renewal session", "Initialize Session");
        }

        data.SessionId = sessionId;
        data.AdfWindowId = adfWindowId;
        data.ViewState = viewState;

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> SubmitAbnStepAsync(RenewalStepData data)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("tmpt:region:0:form:accInput=&");
        formData.Append($"tmpt:region:0:form:it2={data.Abn}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append("event=tmpt%3Aregion%3A0%3Aform%3AnextButt&");
        formData.Append("event.tmpt:region:0:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A0%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var pageContent = await response.Content.ReadAsStringAsync();

        // Check for specific error conditions
        var errorMessage = GetUserFriendlyErrorMessage(pageContent);
        if (errorMessage != null)
        {
            var step = pageContent.Contains("We are processing your renewal application")
                ? "Already Processing"
                : "Not Due For Renewal";
            return StepResult<RenewalStepData>.Failure(errorMessage, step);
        }

        var success = response.IsSuccessStatusCode &&
                     (pageContent.Contains("Business name to be renewed") ||
                      pageContent.Contains("Email required for online payment") ||
                      pageContent.Contains("Select Payment Preference"));

        if (!success)
        {
            return StepResult<RenewalStepData>.Failure("Failed to submit ABN", "Submit ABN");
        }

        data.PageContent = pageContent;
        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> SelectBusinessStepAsync(RenewalStepData data)
    {
        // Find the radio button ID for the specified account number
        var radioButtonId = FindRadioButtonIdByAccountNumber(data.PageContent, data.AccountNumber);

        if (string.IsNullOrEmpty(radioButtonId))
        {
            return StepResult<RenewalStepData>.Failure(
                $"Could not find business name with account number {data.AccountNumber}",
                "Select Business Name");
        }

        // URL encode the radio button ID for form data
        var encodedRadioButtonId = Uri.EscapeDataString(radioButtonId);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"bngrp={encodedRadioButtonId}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("oracle.adf.view.rich.DELTAS=%7Btmpt%3Awaitpopup%3D%7B_shown%3D%7D%7D&");
        formData.Append($"event={encodedRadioButtonId}&");
        formData.Append($"event.{radioButtonId}=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS={encodedRadioButtonId}");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("buttonNextNew"))
        {
            return StepResult<RenewalStepData>.Failure("Failed to select business name", "Select Business Name");
        }

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> ProceedToRenewalPeriodStepAsync(RenewalStepData data)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("bngrp=tmpt%3Aregion%3A1%3Aform%3Asbr1&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("event=tmpt%3Aregion%3A1%3Aform%3AnextBut&");
        formData.Append("event.tmpt:region:1:form:nextBut=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A1%3Aform%3AnextBut");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return StepResult<RenewalStepData>.Failure("HTTP request failed", "Proceed to Renewal Period");
        }

        var content = await response.Content.ReadAsStringAsync();

        // Extract transaction reference from response
        var transactionRef = ExtractTransactionReference(content);

        // Check if we jumped to email page (ASIC resumed session)
        bool jumpedToEmail = content.Contains("Please provide a valid email which is required for ASIC online payment.");

        // Verify we're on a valid page
        bool onRenewalPeriodPage = content.Contains("Renewal period");

        if (!jumpedToEmail && !onRenewalPeriodPage)
        {
            return StepResult<RenewalStepData>.Failure("Unexpected page state", "Proceed to Renewal Period");
        }

        data.TransactionReference = transactionRef;
        data.JumpedToEmail = jumpedToEmail;
        data.PageContent = content;

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> SelectRenewalPeriodStepAsync(RenewalStepData data)
    {
        // Skip if we jumped to email (resumed session)
        if (data.JumpedToEmail)
        {
            return StepResult<RenewalStepData>.Success(data);
        }

        // years: 0 = 1 year, 1 = 3 years
        var periodValue = data.RenewalYears == 3 ? "1" : "0";

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"tmpt:region:2:form:selRen={periodValue}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("event=tmpt%3Aregion%3A2%3Aform%3AselRen&");
        formData.Append("event.tmpt:region:2:form:selRen=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A2%3Aform%3AselRen");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("buttonNextNew"))
        {
            return StepResult<RenewalStepData>.Failure("Failed to select renewal period", "Select Renewal Period");
        }

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> ProceedToReviewStepAsync(RenewalStepData data)
    {
        // Skip if we jumped to email (resumed session)
        if (data.JumpedToEmail)
        {
            return StepResult<RenewalStepData>.Success(data);
        }

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("tmpt:region:2:form:selRen=0&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("event=tmpt%3Aregion%3A2%3Aform%3AnextButt&");
        formData.Append("event.tmpt:region:2:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A2%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("Review"))
        {
            return StepResult<RenewalStepData>.Failure("Failed to proceed to review", "Proceed to Review");
        }

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> SelectAuthorityDeclarationStepAsync(RenewalStepData data)
    {
        // Skip if we jumped to email (resumed session)
        if (data.JumpedToEmail)
        {
            return StepResult<RenewalStepData>.Success(data);
        }

        // Select "Representative declaration" (auth2)
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("auth=tmpt%3Aregion%3A3%3Aauth2&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("event=tmpt%3Aregion%3A3%3Aauth2&");
        formData.Append("event.tmpt:region:3:auth2=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A3%3Aauth2");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("buttonNextNew"))
        {
            return StepResult<RenewalStepData>.Failure("Failed to select authority declaration", "Select Authority Declaration");
        }

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> ProceedToPaymentStepAsync(RenewalStepData data)
    {
        // Skip if we jumped to email (resumed session)
        if (data.JumpedToEmail)
        {
            return StepResult<RenewalStepData>.Success(data);
        }

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("auth=tmpt%3Aregion%3A3%3Aauth2&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("event=tmpt%3Aregion%3A3%3AnextButt&");
        formData.Append("event.tmpt:region:3:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A3%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("Email required for online payment"))
        {
            return StepResult<RenewalStepData>.Failure("Failed to proceed to payment", "Proceed to Payment");
        }

        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> SubmitEmailStepAsync(RenewalStepData data)
    {
        var encodedEmail = Uri.EscapeDataString(data.Email);

        // Extract the actual region index from the current page
        var regionIndex = ExtractCurrentRegionIndex(data.PageContent);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"tmpt:region:{regionIndex}:form:emIn1={encodedEmail}&");
        formData.Append($"tmpt:region:{regionIndex}:form:emIn2={encodedEmail}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("oracle.adf.view.rich.RENDER=tmpt%3Aregion&");
        formData.Append("oracle.adf.view.rich.DELTAS=%7Btmpt%3Awaitpopup%3D%7B_shown%3D%7D%7D&");
        formData.Append($"event=tmpt%3Aregion%3A{regionIndex}%3Aform%3AnextButt&");
        formData.Append($"event.tmpt:region:{regionIndex}:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A{regionIndex}%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("Select Payment Preference"))
        {
            var errorMsg = data.JumpedToEmail
                ? "Failed to submit email (resumed session)"
                : "Failed to submit email";
            return StepResult<RenewalStepData>.Failure(errorMsg, "Submit Email");
        }

        data.PageContent = content;
        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> SelectPaymentMethodStepAsync(RenewalStepData data)
    {
        // Extract the actual region index from the current page
        var regionIndex = ExtractCurrentRegionIndex(data.PageContent);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"popt=tmpt%3Aregion%3A{regionIndex}%3ApayNow&");
        formData.Append($"plopt=tmpt%3Aregion%3A{regionIndex}%3Abpay&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append($"event=tmpt%3Aregion%3A{regionIndex}%3ApayLater%2Ctmpt%3Aregion%3A{regionIndex}%3ApayNow&");
        formData.Append($"event.tmpt:region:{regionIndex}:payLater=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"event.tmpt:region:{regionIndex}:payNow=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A{regionIndex}%3ApayLater%2Ctmpt%3Aregion%3A{regionIndex}%3ApayNow");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode || !content.Contains("Pay Now"))
        {
            return StepResult<RenewalStepData>.Failure("Failed to select payment method", "Select Payment Method");
        }

        data.PageContent = content;
        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> OpenPaymentGatewayStepAsync(RenewalStepData data)
    {
        // Extract the actual region index from the current page
        var regionIndex = ExtractCurrentRegionIndex(data.PageContent);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"popt=tmpt%3Aregion%3A{regionIndex}%3ApayNow&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={data.ViewState}&");
        formData.Append("oracle.adf.view.rich.RENDER=tmpt%3Aregion&");
        formData.Append($"event=tmpt%3Aregion%3A{regionIndex}%3AnextButt&");
        formData.Append($"event.tmpt:region:{regionIndex}:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A{regionIndex}%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Extract payment URL from response
        var paymentUrlMatch = Regex.Match(content, @"'url':'(https://regpayment\.asic\.gov\.au/AsicPayment/faces/index\.jspx\?[^']+)'");
        var paymentUrl = paymentUrlMatch.Success ? paymentUrlMatch.Groups[1].Value : "";

        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(paymentUrl))
        {
            return StepResult<RenewalStepData>.Failure("Failed to open payment gateway", "Open Payment Gateway");
        }

        data.PaymentUrl = paymentUrl;
        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<StepResult<RenewalStepData>> ProcessPaymentStepAsync(RenewalStepData data)
    {
        var paymentResult = await _paymentClient.ProcessPaymentAsync(
            data.PaymentUrl,
            data.TransactionReference,
            data.CardDetails);

        if (!paymentResult.Success)
        {
            var errorMsg = $"Renewal initiated but payment failed: {paymentResult.Message}. " +
                          $"Transaction reference: {data.TransactionReference ?? "Unknown"}";
            return StepResult<RenewalStepData>.Failure(errorMsg, "Process Payment");
        }

        data.HostedTokenizationId = paymentResult.HostedTokenizationId;
        return StepResult<RenewalStepData>.Success(data);
    }

    private async Task<(string SessionId, string AdfWindowId, string ViewState)> InitializeSessionAsync()
    {
        // Step 1: Initial GET to renewal page
        var response = await _http.GetAsync("public/faces/renewal");
        var content = await response.Content.ReadAsStringAsync();

        // Extract session ID from Set-Cookie header
        var sessionId = response.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(x => x.StartsWith("JSESSIONID="))?
            .Split(';')[0]
            .Replace("JSESSIONID=", "");

        // Extract parameters from JavaScript
        var afrLoop = Regex.Match(content, @"'_afrLoop',\s*'(\d+)'").Groups[1].Value;
        var afrPage = Regex.Match(content, @"'_afrPage',\s*'',\s*'(\w+)'").Groups[1].Value;

        // Step 2: GET with full parameters
        var url = $"public/faces/renewal;jsessionid={sessionId}?_afrLoop={afrLoop}&_afrWindowMode=2&Adf-Window-Id={afrPage}&_afrFS=16&_afrMT=screen&_afrMFW=1865&_afrMFH=926&_afrMFDW=1920&_afrMFDH=1080&_afrMFC=8&_afrMFCI=0&_afrMFM=0&_afrMFR=96&_afrMFG=0&_afrMFS=0&_afrMFO=0";

        response = await _http.GetAsync(url);
        content = await response.Content.ReadAsStringAsync();

        var document = await _htmlParser.ParseDocumentAsync(content);
        var adfWindowId = document.QuerySelector("input[name='Adf-Window-Id']")?.GetAttribute("value");
        var viewState = document.QuerySelector("input[name='javax.faces.ViewState']")?.GetAttribute("value");

        return (sessionId, adfWindowId, viewState);
    }

    private async Task<(bool Success, string Content)> SubmitAbnForSearchAsync(string abn, string adfWindowId, string viewState)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("tmpt:region:0:form:accInput=&");
        formData.Append($"tmpt:region:0:form:it2={abn}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("Adf-Page-Id=1&");
        formData.Append("event=tmpt%3Aregion%3A0%3Aform%3AnextButt&");
        formData.Append("event.tmpt:region:0:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A0%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=1");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "3");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        var success = response.IsSuccessStatusCode && content.Contains("Business name to be renewed");

        return (success, content);
    }

    private List<BusinessName> ParseBusinessNamesFromResponse(string xmlContent)
    {
        var businessNames = new List<BusinessName>();

        // Find all dataText spans - each table row has 3 spans (name, account, date)
        var dataTextMatches = Regex.Matches(xmlContent, @"<span class=""dataText"">([^<]+)</span>");

        if (dataTextMatches.Count < 3)
        {
            return businessNames;
        }

        // Parse business names in groups of 3 (each row in the table)
        // Row format: [Business Name, Account Number, Registration Date]
        for (int i = 0; i < dataTextMatches.Count; i += 3)
        {
            // Make sure we have all 3 values for this row
            if (i + 2 >= dataTextMatches.Count)
            {
                break;
            }

            var name = dataTextMatches[i].Groups[1].Value;
            var accountNumber = dataTextMatches[i + 1].Groups[1].Value;
            var registrationDate = dataTextMatches[i + 2].Groups[1].Value;

            businessNames.Add(new BusinessName
            {
                Name = name,
                AccountNumber = accountNumber,
                RegistrationDate = registrationDate
            });
        }

        return businessNames;
    }

    private string ExtractTransactionReference(string content)
    {
        var match = Regex.Match(content, @"Transaction reference number:<\/strong>\s*([A-Z0-9\-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private string ExtractCurrentRegionIndex(string content)
    {
        // Look for patterns like: tmpt:region:N:form:componentId or tmpt:region:N:componentId
        var match = Regex.Match(content, @"tmpt:region:(\d+):[^:]*");
        return match.Success ? match.Groups[1].Value : "0";
    }

    private string FindRadioButtonIdByAccountNumber(string content, string accountNumber)
    {
        // The HTML structure has radio buttons followed by business data
        // Each row contains: radio button, business name, account number, registration date
        // Radio button pattern: <span id="tmpt:region:1:form:sbr1..." class="selectBooleanRadioHiddenLabel"
        // Data pattern: <span class="dataText">VALUE</span>

        // Find all radio button IDs
        var radioMatches = Regex.Matches(content, @"<span id=""(tmpt:region:\d+:form:sbr[^""]+)""[^>]*class=""selectBooleanRadioHiddenLabel");

        // Find all dataText spans (business names, account numbers, registration dates)
        var dataTextMatches = Regex.Matches(content, @"<span class=""dataText"">([^<]+)</span>");

        // Match radio buttons to account numbers
        // Each table row has 3 dataText spans: [name, account, date]
        for (int i = 0; i < radioMatches.Count && (i * 3 + 1) < dataTextMatches.Count; i++)
        {
            var accountIndex = i * 3 + 1; // Account number is the second item (index 1) in each group
            var accountValue = dataTextMatches[accountIndex].Groups[1].Value.Trim();

            if (accountValue == accountNumber)
            {
                return radioMatches[i].Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks the response content for known ASIC error messages and returns a user-friendly error message.
    /// Returns null if no specific error pattern is found.
    /// </summary>
    private string GetUserFriendlyErrorMessage(string content)
    {
        if (content.Contains("We are processing your renewal application"))
        {
            return "A renewal for this ABN is already in progress. " +
                   "Please complete or cancel the existing renewal before starting a new one. " +
                   "Check your email for the invoice or wait 48 hours for processing to complete.";
        }

        if (content.Contains("This business name registration is not due for renewal"))
        {
            return "This business name is not due for renewal yet. " +
                   "ASIC will send a renewal notice when it becomes due. " +
                   "Check the business name register to see the next renewal date.";
        }

        return null;
    }
}
