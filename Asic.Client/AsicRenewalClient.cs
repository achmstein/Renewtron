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

    // Business name renewal - Complete flow with error handling
    public async Task<RenewalResult> RenewBusinessNameAsync(string abn, string businessName, int renewalYears, string email, CreditCardDetails cardDetails)
    {
        try
        {
            // Step 1: Initialize renewal session (always starts fresh)
            var (sessionId, adfWindowId, viewState) = await InitializeRenewalSessionAsync();
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

            // Detect if we've jumped to a later step (ASIC session state)
            var currentStep = DetectCurrentStep(businessListResult.Content);
            string transactionReference = null;

            if (currentStep == "Email")
            {
                // ASIC jumped directly to email/payment step
                // Extract transaction reference if available
                transactionReference = ExtractTransactionReference(businessListResult.Content);

                // Skip to email submission
                var emailResult = await SubmitEmailAsync(email, adfWindowId, viewState);
                if (!emailResult.Success)
                {
                    return RenewalResult.Failed("Failed to submit email (resumed session)", "Submit Email");
                }

                // Continue with payment
                var paymentMethodResult = await SelectPaymentMethodAsync(adfWindowId, viewState);
                if (!paymentMethodResult.Success)
                {
                    return RenewalResult.Failed("Failed to select payment method (resumed session)", "Select Payment Method");
                }

                var paymentGatewayResult = await OpenPaymentGatewayAsync(adfWindowId, viewState);
                if (!paymentGatewayResult.Success)
                {
                    return RenewalResult.Failed("Failed to open payment gateway (resumed session)", "Open Payment Gateway");
                }

                var paymentResult = await _paymentClient.ProcessPaymentAsync(
                    paymentGatewayResult.PaymentUrl,
                    cardDetails);

                if (!paymentResult.Success)
                {
                    return RenewalResult.Failed(
                        $"Renewal initiated but payment failed: {paymentResult.Message}. " +
                        $"Transaction reference: {transactionReference ?? "Unknown"}",
                        "Process Payment");
                }

                return RenewalResult.Success(
                    transactionReference ?? "RESUMED",
                    paymentResult.HostedTokenizationId);
            }

            // Normal flow continues...

            // Step 3: Select specific business name from the list
            var selectionResult = await SelectBusinessNameAsync(businessName, adfWindowId, viewState);
            if (!selectionResult.Success)
            {
                return RenewalResult.Failed("Failed to select business name", "Select Business Name");
            }

            // Step 4: Click next to proceed to renewal period selection
            var periodPageResult = await ProceedToRenewalPeriodAsync(adfWindowId, viewState);
            if (!periodPageResult.Success)
            {
                return RenewalResult.Failed("Failed to proceed to renewal period", "Proceed to Renewal Period");
            }

            transactionReference = periodPageResult.TransactionReference;

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

            // Step 9: Enter email and confirm email
            var emailResult2 = await SubmitEmailAsync(email, adfWindowId, viewState);
            if (!emailResult2.Success)
            {
                return RenewalResult.Failed("Failed to submit email", "Submit Email");
            }

            // Step 10: Select payment method (Pay now by credit card)
            var paymentMethodResult2 = await SelectPaymentMethodAsync(adfWindowId, viewState);
            if (!paymentMethodResult2.Success)
            {
                return RenewalResult.Failed("Failed to select payment method", "Select Payment Method");
            }

            // Step 11: Click Pay Now to open payment gateway
            var paymentGatewayResult2 = await OpenPaymentGatewayAsync(adfWindowId, viewState);
            if (!paymentGatewayResult2.Success)
            {
                return RenewalResult.Failed("Failed to open payment gateway", "Open Payment Gateway");
            }

            // Step 12: Process payment through payment gateway
            var paymentResult2 = await _paymentClient.ProcessPaymentAsync(
                paymentGatewayResult2.PaymentUrl,
                cardDetails);

            if (!paymentResult2.Success)
            {
                return RenewalResult.Failed(
                    $"Renewal initiated but payment failed: {paymentResult2.Message}. " +
                    $"Transaction reference: {transactionReference}",
                    "Process Payment");
            }

            return RenewalResult.Success(
              transactionReference,
              paymentResult2.HostedTokenizationId);
        }
        catch (Exception ex)
        {
            return RenewalResult.Failed($"Renewal failed: {ex.Message}", "Exception");
        }
    }

    // Detect which step ASIC is currently on based on response content
    private string DetectCurrentStep(string content)
    {
        if (content.Contains("Email required for online payment") ||
            content.Contains("Select Payment Preference"))
        {
            return "Email";
        }
        if (content.Contains("Renewal period"))
        {
            return "RenewalPeriod";
        }
        if (content.Contains("Review"))
        {
            return "Review";
        }
        if (content.Contains("Business name to be renewed"))
        {
            return "SelectBusinessName";
        }
        return "Normal";
    }

    // Extract transaction reference from any response
    private string ExtractTransactionReference(string content)
    {
        var match = Regex.Match(content, @"Transaction reference number:<\/strong>\s*([A-Z0-9\-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    async Task<(string SessionId, string AdfWindowId, string ViewState)> InitializeRenewalSessionAsync()
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

    async Task<(bool Success, string Message, string Content)> SubmitAbnForRenewalAsync(string abn, string adfWindowId, string viewState)
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
            return (false, "We are processing your renewal application", content);
        }

        var success = response.IsSuccessStatusCode &&
                     (content.Contains("Business name to be renewed") ||
                      content.Contains("Email required for online payment") ||
                      content.Contains("Select Payment Preference"));

        return (success, success ? "Success" : "Failed", content);
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

    async Task<(bool Success, string TransactionReference)> ProceedToRenewalPeriodAsync(string adfWindowId, string viewState)
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
        var content = await response.Content.ReadAsStringAsync();

        // Extract transaction reference from response
        var transactionRef = Regex.Match(content, @"Transaction reference number:<\/strong>\s*([A-Z0-9\-]+)").Groups[1].Value;

        return (response.IsSuccessStatusCode && content.Contains("Renewal period"), transactionRef);
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

    async Task<(bool Success, string Message)> SubmitEmailAsync(string email, string adfWindowId, string viewState)
    {
        var encodedEmail = Uri.EscapeDataString(email);

        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append($"tmpt:region:4:form:emIn1={encodedEmail}&");
        formData.Append($"tmpt:region:4:form:emIn2={encodedEmail}&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("oracle.adf.view.rich.RENDER=tmpt%3Aregion&");
        formData.Append("oracle.adf.view.rich.DELTAS=%7Btmpt%3Awaitpopup%3D%7B_shown%3D%7D%7D&");
        formData.Append("event=tmpt%3Aregion%3A4%3Aform%3AnextButt&");
        formData.Append("event.tmpt:region:4:form:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A4%3Aform%3AnextButt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("Select Payment Preference"), "Success");
    }

    async Task<(bool Success, string Message)> SelectPaymentMethodAsync(string adfWindowId, string viewState)
    {
        // Select "Pay now by Credit Card"
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("popt=tmpt%3Aregion%3A5%3ApayNow&");
        formData.Append("plopt=tmpt%3Aregion%3A5%3Abpay&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("event=tmpt%3Aregion%3A5%3ApayLater%2Ctmpt%3Aregion%3A5%3ApayNow&");
        formData.Append("event.tmpt:region:5:payLater=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("event.tmpt:region:5:payNow=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A5%3ApayLater%2Ctmpt%3Aregion%3A5%3ApayNow");

        var request = new HttpRequestMessage(HttpMethod.Post, $"public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=0");
        request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.Add("Adf-Rich-Message", "true");
        request.Headers.Add("Adf-Ads-Page-Id", "1");

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode && content.Contains("Pay Now"), "Success");
    }

    async Task<(bool Success, string PaymentUrl)> OpenPaymentGatewayAsync(string adfWindowId, string viewState)
    {
        var formData = new StringBuilder();
        formData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
        formData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
        formData.Append("popt=tmpt%3Aregion%3A5%3ApayNow&");
        formData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
        formData.Append($"Adf-Window-Id={adfWindowId}&");
        formData.Append("Adf-Page-Id=0&");
        formData.Append($"javax.faces.ViewState={viewState}&");
        formData.Append("oracle.adf.view.rich.RENDER=tmpt%3Aregion&");
        formData.Append("event=tmpt%3Aregion%3A5%3AnextButt&");
        formData.Append("event.tmpt:region:5:nextButt=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
        formData.Append("oracle.adf.view.rich.PROCESS=tmpt%3Aregion%2Ctmpt%3Aregion%3A5%3AnextButt");

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
}