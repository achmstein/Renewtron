using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Asic.Client;

public class AsicRenewalClient : IAsicRenewalClient
{
    private readonly HttpClient _http;
    private readonly HtmlParser _htmlParser;
    private readonly IAsicPaymentClient _paymentClient;

    public AsicRenewalClient(IAsicPaymentClient paymentClient)
    {
        _http = new HttpClient()
        {
            BaseAddress = new Uri("https://asicconnect.asic.gov.au/"),
            Timeout = TimeSpan.FromSeconds(300),
        };

        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");

        _htmlParser = new HtmlParser();
        _paymentClient = paymentClient;
    }

    public async Task<BusinessNamesResult> SearchAsync(string abn)
    {
        try
        {
            // Step 1: Initialize renewal session
            var (sessionId, adfWindowId, viewState) = await InitializeSessionAsync();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(adfWindowId) || string.IsNullOrEmpty(viewState))
            {
                return BusinessNamesResult.Failed("Failed to initialize renewal session");
            }

            // Step 2: Submit ABN to get business name list
            var (success, content) = await SubmitSearchAsync(abn, adfWindowId, viewState);
            if (!success)
            {
                // Check for specific error messages and provide user-friendly responses
                if (content.Contains("We are processing your renewal application"))
                {
                    return BusinessNamesResult.Failed(
                        "A renewal for this ABN is already in progress. " +
                        "Please complete or cancel the existing renewal before starting a new one. " +
                        "Check your email for the invoice or wait 48 hours for processing to complete.");
                }

                if (content.Contains("This business name registration is not due for renewal"))
                {
                    return BusinessNamesResult.Failed(
                        "This business name is not due for renewal yet. " +
                        "ASIC will send a renewal notice when it becomes due. " +
                        "Check the business name register to see the next renewal date.");
                }

                return BusinessNamesResult.Failed("Failed to search for business names. Please check the ABN and try again.");
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

    private async Task<(bool Success, string Content)> SubmitSearchAsync(string abn, string adfWindowId, string viewState)
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

    public async Task<RenewalResult> RenewBusinessNameAsync(string abn, string businessName, int renewalYears, string email, CreditCardDetails cardDetails)
    {
        try
        {
            // Step 1: Initialize renewal session (always starts fresh)
            var (sessionId, adfWindowId, viewState) = await InitializeSessionAsync();
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(adfWindowId) || string.IsNullOrEmpty(viewState))
            {
                return RenewalResult.Failed("Failed to initialize renewal session", "Initialize Session");
            }

            // Step 2: Submit ABN to get business name list
            var businessListResult = await SubmitAbnForRenewalAsync(abn, adfWindowId, viewState);

            // Check for "already processing" error
            if (!businessListResult.Success)
            {
                if (businessListResult.Message.Contains("We are processing your renewal application"))
                {
                    return RenewalResult.Failed(
                        "A renewal for this ABN is already in progress. " +
                        "Please complete or cancel the existing renewal before starting a new one. " +
                        "Check your email for the invoice or wait 48 hours for processing to complete.",
                        "Already Processing");
                }

                if (businessListResult.Message.Contains("This business name registration is not due for renewal"))
                {
                    return RenewalResult.Failed(
                        "This business name is not due for renewal yet. " +
                        "ASIC will send a renewal notice when it becomes due. " +
                        "Check the business name register to see the next renewal date.",
                        "Not Due For Renewal");
                }

                return RenewalResult.Failed($"Failed to submit ABN: {businessListResult.Message}", "Submit ABN");
            }

            // Step 3: Select specific business name from the list
            var selectionResult = await SelectBusinessNameAsync(businessName, adfWindowId, viewState);
            if (!selectionResult.Success)
            {
                return RenewalResult.Failed("Failed to select business name", "Select Business Name");
            }

            // Step 4: Click next to proceed to renewal period selection (or jump to email if resumed)
            var periodPageResult = await ProceedToRenewalPeriodAsync(adfWindowId, viewState);
            if (!periodPageResult.Success)
            {
                return RenewalResult.Failed("Failed to proceed to renewal period", "Proceed to Renewal Period");
            }

            string transactionReference = periodPageResult.TransactionReference;

            // Normal flow - we're on renewal period page
            if (!periodPageResult.JumpedToEmail)
            {
                // Step 5: Select renewal period (1 or 3 years)
                var periodResult = await SelectRenewalPeriodAsync(renewalYears, adfWindowId, viewState);
                if (!periodResult.Success)
                {
                    return RenewalResult.Failed("Failed to select renewal period", "Select Renewal Period");
                }

                // Step 6: Click next to proceed to review page
                var reviewPageResult = await ProceedToReviewAsync(adfWindowId, viewState);
                if (!reviewPageResult.Success)
                {
                    return RenewalResult.Failed("Failed to proceed to review", "Proceed to Review");
                }

                // Step 7: Select authority declaration (Representative declaration)
                var authorityResult = await SelectAuthorityDeclarationAsync(adfWindowId, viewState);
                if (!authorityResult.Success)
                {
                    return RenewalResult.Failed("Failed to select authority declaration", "Select Authority Declaration");
                }

                // Step 8: Click next to proceed to payment page
                var paymentPageResult = await ProceedToPaymentAsync(adfWindowId, viewState);
                if (!paymentPageResult.Success)
                {
                    return RenewalResult.Failed("Failed to proceed to payment", "Proceed to Payment");
                }
            }

            // Both flows converge here: Email submission and payment

            // Step 9: Enter email and confirm email (normal flow) or Step 5: Enter email (resumed flow)
            var emailResult = await SubmitEmailAsync(email, adfWindowId, viewState, periodPageResult.Content);
            if (!emailResult.Success)
            {
                return RenewalResult.Failed(
                    periodPageResult.JumpedToEmail ? "Failed to submit email (resumed session)" : "Failed to submit email",
                    "Submit Email");
            }

            // Step 10: Select payment method (Pay now by credit card) - both flows
            var paymentMethodResult = await SelectPaymentMethodAsync(adfWindowId, viewState, emailResult.Content);
            if (!paymentMethodResult.Success)
            {
                return RenewalResult.Failed("Failed to select payment method", "Select Payment Method");
            }

            // Step 11: Click Pay Now to open payment gateway - both flows
            var paymentGatewayResult = await OpenPaymentGatewayAsync(adfWindowId, viewState, paymentMethodResult.Content);
            if (!paymentGatewayResult.Success)
            {
                return RenewalResult.Failed("Failed to open payment gateway", "Open Payment Gateway");
            }

            // Step 12: Process payment through payment gateway - both flows
            var paymentResult = await _paymentClient.ProcessPaymentAsync(
                paymentGatewayResult.PaymentUrl,
                periodPageResult.TransactionReference,
                adfWindowId,
                cardDetails);

            if (!paymentResult.Success)
            {
                return RenewalResult.Failed(
                    $"Renewal initiated but payment failed: {paymentResult.Message}. " +
                    $"Transaction reference: {transactionReference ?? "Unknown"}",
                    "Process Payment");
            }

            return RenewalResult.Success(
                transactionReference ?? (periodPageResult.JumpedToEmail ? "RESUMED" : "Unknown"),
                paymentResult.HostedTokenizationId);
        }
        catch (Exception ex)
        {
            return RenewalResult.Failed($"Renewal failed: {ex.Message}", "Exception");
        }
    }

    private string ExtractTransactionReference(string content)
    {
        var match = Regex.Match(content, @"Transaction reference number:<\/strong>\s*([A-Z0-9\-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    async Task<(string SessionId, string AdfWindowId, string ViewState)> InitializeSessionAsync()
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

    async Task<(bool Success, string Message)> SubmitAbnForRenewalAsync(string abn, string adfWindowId, string viewState)
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
        formData.Append("Adf-Page-Id=0&");
        formData.Append("event=tmpt%3Aregion%3A0%3Aform%3AnextButt&");
        formData.Append("event.tmpt:region:0:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A0%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Check for "already processing" error
        if (content.Contains("We are processing your renewal application"))
        {
            return (false, "We are processing your renewal application");
        }

        var success = response.IsSuccessStatusCode &&
                     (content.Contains("Business name to be renewed") ||
                      content.Contains("Email required for online payment") ||
                      content.Contains("Select Payment Preference"));

        return (success, success ? "Success" : "Failed");
    }

    async Task<(bool Success, string Message)> SelectBusinessNameAsync(string businessName, string adfWindowId, string viewState)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("bngrp=tmpt%3Aregion%3A1%3Aform%3Asbr1&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("oracle.adf.view.rich.DELTAS=%7Btmpt%3Awaitpopup%3D%7B_shown%3D%7D%7D&");
        formData.Append("event=tmpt%3Aregion%3A1%3Aform%3Asbr1&");
        formData.Append("event.tmpt:region:1:form:sbr1=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A1%3Aform%3Asbr1");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("buttonNextNew"),
                response.IsSuccessStatusCode ? "Success" : "Failed");
    }
  
    async Task<RenewalPeriodResult> ProceedToRenewalPeriodAsync(string adfWindowId, string viewState)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("bngrp=tmpt%3Aregion%3A1%3Aform%3Asbr1&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("event=tmpt%3Aregion%3A1%3Aform%3AnextBut&");
        formData.Append("event.tmpt:region:1:form:nextBut=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A1%3Aform%3AnextBut");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return RenewalPeriodResult.Failed("HTTP request failed");
        }

        var content = await response.Content.ReadAsStringAsync();

        // Extract transaction reference from response
        var transactionRef = ExtractTransactionReference(content);

        // Check if we jumped to email page (ASIC resumed session)
        bool jumpedToEmail = content.Contains("Please provide a valid email which is required for ASIC online payment.") ||
                             content.Contains("Email required for online payment");

        // Verify we're on a valid page
        bool onRenewalPeriodPage = content.Contains("Renewal period");

        if (!jumpedToEmail && !onRenewalPeriodPage)
        {
            return RenewalPeriodResult.Failed("Unexpected page state");
        }

        return RenewalPeriodResult.Succeeded(transactionRef, content, jumpedToEmail);
    }

    async Task<(bool Success, string Message)> SelectRenewalPeriodAsync(int years, string adfWindowId, string viewState)
    {
        // years: 0 = 1 year, 1 = 3 years
        var periodValue = years == 3 ? "1" : "0";

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"tmpt:region:2:form:selRen={periodValue}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("event=tmpt%3Aregion%3A2%3Aform%3AselRen&");
        formData.Append("event.tmpt:region:2:form:selRen=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A2%3Aform%3AselRen");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("buttonNextNew"), "Success");
    }

    async Task<(bool Success, string Message)> ProceedToReviewAsync(string adfWindowId, string viewState)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("tmpt:region:2:form:selRen=0&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("event=tmpt%3Aregion%3A2%3Aform%3AnextButt&");
        formData.Append("event.tmpt:region:2:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A2%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("Review"), "Success");
    }

    async Task<(bool Success, string Message)> SelectAuthorityDeclarationAsync(string adfWindowId, string viewState)
    {
        // Select "Representative declaration" (auth2)
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("auth=tmpt%3Aregion%3A3%3Aauth2&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("event=tmpt%3Aregion%3A3%3Aauth2&");
        formData.Append("event.tmpt:region:3:auth2=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A3%3Aauth2");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("buttonNextNew"), "Success");
    }

    async Task<(bool Success, string Message)> ProceedToPaymentAsync(string adfWindowId, string viewState)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("auth=tmpt%3Aregion%3A3%3Aauth2&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("event=tmpt%3Aregion%3A3%3AnextButt&");
        formData.Append("event.tmpt:region:3:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A3%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("Email required for online payment"), "Success");
    }

    async Task<(bool Success, string Message, string Content)> SubmitEmailAsync(string email, string adfWindowId, string viewState, string pageContent)
    {
        var encodedEmail = Uri.EscapeDataString(email);

        // Extract the actual region index from the current page
        var regionIndex = ExtractCurrentRegionIndex(pageContent);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"tmpt:region:{regionIndex}:form:emIn1={encodedEmail}&");
        formData.Append($"tmpt:region:{regionIndex}:form:emIn2={encodedEmail}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("oracle.adf.view.rich.RENDER=tmpt%3Aregion&");
        formData.Append("oracle.adf.view.rich.DELTAS=%7Btmpt%3Awaitpopup%3D%7B_shown%3D%7D%7D&");
        formData.Append($"event=tmpt%3Aregion%3A{regionIndex}%3Aform%3AnextButt&");
        formData.Append($"event.tmpt:region:{regionIndex}:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A{regionIndex}%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("Select Payment Preference"), "Success", content);
    }

    async Task<(bool Success, string Message, string Content)> SelectPaymentMethodAsync(string adfWindowId, string viewState, string pageContent)
    {
        // Extract the actual region index from the current page
        var regionIndex = ExtractCurrentRegionIndex(pageContent);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"popt=tmpt%3Aregion%3A{regionIndex}%3ApayNow&");
        formData.Append($"plopt=tmpt%3Aregion%3A{regionIndex}%3Abpay&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append($"event=tmpt%3Aregion%3A{regionIndex}%3ApayLater%2Ctmpt%3Aregion%3A{regionIndex}%3ApayNow&");
        formData.Append($"event.tmpt:region:{regionIndex}:payLater=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"event.tmpt:region:{regionIndex}:payNow=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A{regionIndex}%3ApayLater%2Ctmpt%3Aregion%3A{regionIndex}%3ApayNow");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("Pay Now"), "Success", content);
    }

    async Task<(bool Success, string PaymentUrl)> OpenPaymentGatewayAsync(string adfWindowId, string viewState, string pageContent)
    {
        // Extract the actual region index from the current page
        var regionIndex = ExtractCurrentRegionIndex(pageContent);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"popt=tmpt%3Aregion%3A{regionIndex}%3ApayNow&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("oracle.adf.view.rich.RENDER=tmpt%3Aregion&");
        formData.Append($"event=tmpt%3Aregion%3A{regionIndex}%3AnextButt&");
        formData.Append($"event.tmpt:region:{regionIndex}:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A{regionIndex}%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Extract payment URL from response
        var paymentUrlMatch = Regex.Match(content, @"'url':'(https://regpayment\.asic\.gov\.au/AsicPayment/faces/index\.jspx\?[^']+)'");
        var paymentUrl = paymentUrlMatch.Success ? paymentUrlMatch.Groups[1].Value : "";

        return (response.IsSuccessStatusCode && !string.IsNullOrEmpty(paymentUrl), paymentUrl);
    }

    private string ExtractCurrentRegionIndex(string content)
    {
        // Look for patterns like: tmpt:region:N:form:componentId or tmpt:region:N:componentId
        var match = Regex.Match(content, @"tmpt:region:(\d+):[^:]*");
        return match.Success ? match.Groups[1].Value : "0";
    }
}
