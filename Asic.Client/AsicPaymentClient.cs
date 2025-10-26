using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Asic.Client;
public class AsicPaymentClient : IAsicPaymentClient
{
    private readonly HttpClient _http;
    private readonly HtmlParser _htmlParser;

    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0";

    public AsicPaymentClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true
        };

        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

        _htmlParser = new HtmlParser();
    }

    public async Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, string adfWindowId, CreditCardDetails cardDetails)
    {
        try
        {
            var initialData = PaymentStepData.Create(paymentUrl, sessionId, adfWindowId, cardDetails);

            // Railway-oriented pipeline: each step only executes if the previous step succeeded
            return await InitializeSessionStepAsync(initialData)
                .ThenAsync("Load Tokenization Form", LoadTokenizationFormStepAsync)
                .ThenAsync("Get Card Information", GetCardInformationStepAsync)
                .ThenAsync("Prepare 3DS", PrepareThreeDSStepAsync)
                .ThenAsync("Submit Initial Payment", SubmitInitialPaymentStepAsync)
                .ThenAsync("Submit Tokenization", SubmitTokenizationStepAsync)
                .ThenAsync("Complete Payment", CompletePaymentStepAsync)
                .ToPaymentResultAsync(data => PaymentResult.Succeeded(data.HostedTokenizationId));
        }
        catch (Exception ex)
        {
            return PaymentResult.Failed($"Payment processing failed: {ex.Message}");
        }
    }


    private async Task<StepResult<PaymentStepData>> InitializeSessionStepAsync(PaymentStepData data)
    {
        var (success, message, tokenizationFormUrl, adfWindowId, viewState) = await InitializeSessionAsync(data.PaymentUrl);

        if (!success)
        {
            return StepResult<PaymentStepData>.Failure($"Failed to navigate to payment page: {message}", "Initialize Session");
        }

        if (string.IsNullOrEmpty(tokenizationFormUrl))
        {
            return StepResult<PaymentStepData>.Failure("Failed to extract tokenization form URL", "Initialize Session");
        }

        data.TokenizationFormUrl = tokenizationFormUrl;
        data.PaymentAdfWindowId = adfWindowId;
        data.ViewState = viewState;

        return StepResult<PaymentStepData>.Success(data);
    }

    private async Task<StepResult<PaymentStepData>> LoadTokenizationFormStepAsync(PaymentStepData data)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, data.TokenizationFormUrl);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Referer", "https://regpayment.asic.gov.au/");

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Load Tokenization Form");
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Load Tokenization Form");
        }
    }

    private async Task<StepResult<PaymentStepData>> GetCardInformationStepAsync(PaymentStepData data)
    {
        try
        {
            // Extract the token from the URL
            var token = ExtractTokenFromUrl(data.TokenizationFormUrl);
            var binUrl = $"https://payment.anzworldline-solutions.com.au/hostedtokenization/bin/getcardinformation/{token}";

            // Get first 13 digits for BIN lookup
            var cleanCardNumber = data.CardDetails.CardNumber.Replace(" ", "");
            var firstDigits = cleanCardNumber.Substring(0, Math.Min(13, cleanCardNumber.Length));

            var formData = $"firstdigits={firstDigits}&partialDetection=false";

            var request = new HttpRequestMessage(HttpMethod.Post, binUrl);
            request.Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded");
            request.Headers.Add("Accept", "*/*");
            request.Headers.Add("Origin", "https://payment.anzworldline-solutions.com.au");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Referer", data.TokenizationFormUrl);

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Get Card Information");
            }

            // Parse the JSON response
            var json = JsonDocument.Parse(content);
            var brands = json.RootElement.GetProperty("brands");

            if (brands.GetArrayLength() == 0)
            {
                return StepResult<PaymentStepData>.Failure("No card brand detected", "Get Card Information");
            }

            var brandName = brands[0].GetProperty("name").GetString();
            data.BrandName = brandName;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Get Card Information");
        }
    }

    private async Task<StepResult<PaymentStepData>> PrepareThreeDSStepAsync(PaymentStepData data)
    {
        try
        {
            var token = ExtractTokenFromUrl(data.TokenizationFormUrl);
            var threeDsUrl = $"https://payment.anzworldline-solutions.com.au/hostedtokenization/ThreeDSecure/PrepareThreeDS/{token}";

            var payload = new
            {
                cardNumber = data.CardDetails.CardNumber + " ",
                brandName = data.BrandName
            };

            var jsonContent = JsonSerializer.Serialize(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, threeDsUrl);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            request.Headers.Add("Accept", "*/*");
            request.Headers.Add("Origin", "https://payment.anzworldline-solutions.com.au");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Referer", data.TokenizationFormUrl);

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Prepare 3DS");
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Prepare 3DS");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitInitialPaymentStepAsync(PaymentStepData data)
    {
        try
        {
            var formData = new StringBuilder();
            formData.Append("org.apache.myfaces.trinidad.faces.FORM=frmASICPayment&");
            formData.Append($"Adf-Window-Id={data.PaymentAdfWindowId}&");
            formData.Append($"javax.faces.ViewState={data.ViewState}&");
            formData.Append("Adf-Page-Id=3&");
            formData.Append("event=r1%3A0%3AsubmitBtn&");
            formData.Append("event.r1:0:submitBtn=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
            formData.Append("oracle.adf.view.rich.PROCESS=r1%2Cr1%3A0%3AsubmitBtn");

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?Adf-Window-Id={data.PaymentAdfWindowId}&Adf-Page-Id=3");

            request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            request.Headers.Add("Adf-Rich-Message", "true");
            request.Headers.Add("Adf-Ads-Page-Id", "5");
            request.Headers.Add("Origin", "https://regpayment.asic.gov.au");
            request.Headers.Add("Referer", "https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx");

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure("Failed", "Submit Initial Payment");
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit Initial Payment");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitTokenizationStepAsync(PaymentStepData data)
    {
        try
        {
            var token = ExtractTokenFromUrl(data.TokenizationFormUrl);
            var submitUrl = $"https://payment.anzworldline-solutions.com.au/hostedtokenization/Tokenization/Submit/{token}";

            // Build form data
            var formData = new StringBuilder();
            formData.Append("isTemporary=false&");
            formData.Append($"brand={data.BrandName}&");
            formData.Append($"cardnumber={Regex.Replace(data.CardDetails.CardNumber, ".{4}", "$0 ").TrimEnd()}&");
            formData.Append("browserColorDepth=24&");
            formData.Append("browserJavaEnabled=false&");
            formData.Append("browserLanguage=en-US&");
            formData.Append("browserScreenHeight=1080&");
            formData.Append("browserScreenWidth=1920&");
            formData.Append("browserTimeZone=-180&");
            formData.Append("cobadging=&");
            formData.Append("cobadding-indicator=default&");
            formData.Append($"selected-brand-for-groupcards={data.BrandName}&");
            formData.Append($"cardholdername={data.CardDetails.CardholderName.Replace(" ", "+")}&");
            formData.Append($"cardexpirationmonth={data.CardDetails.ExpiryMonth}&");
            formData.Append($"cardexpirationyear={data.CardDetails.ExpiryYear}&");
            formData.Append($"cvc={data.CardDetails.Cvc}");

            var request = new HttpRequestMessage(HttpMethod.Post, submitUrl);
            request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            if (request.Content.Headers.ContentType != null)
            {
                request.Content.Headers.ContentType.CharSet = null;
            }
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Referer", data.TokenizationFormUrl);
            request.Headers.TryAddWithoutValidation("Origin", "https://payment.anzworldline-solutions.com.au");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
            request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Microsoft Edge\";v=\"141\", \"Not?A_Brand\";v=\"8\", \"Chromium\";v=\"141\"");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Storage-Access", "none");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Submit Tokenization");
            }

            // Parse response to get hostedTokenizationId
            var json = JsonDocument.Parse(content);
            var hostedTokenizationId = json.RootElement.GetProperty("hostedTokenizationId").GetString();
            data.HostedTokenizationId = hostedTokenizationId;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit Tokenization");
        }
    }

    private async Task<StepResult<PaymentStepData>> CompletePaymentStepAsync(PaymentStepData data)
    {
        var deviceInfo = CreateDeviceInfo();
        var result = await CompletePaymentAsync(deviceInfo, data.PaymentUrl, data.SessionId,
            data.HostedTokenizationId, data.AdfWindowId, data.ViewState);

        if (!result.Success)
        {
            return StepResult<PaymentStepData>.Failure($"Failed to complete payment: {result.Message}", "Complete Payment");
        }

        return StepResult<PaymentStepData>.Success(data);
    }

    private async Task<(bool Success, string Message, string TokenizationFormUrl, string AdfWindowId, string ViewState)> InitializeSessionAsync(string paymentUrl)
    {
        try
        {
            var uri = new Uri(paymentUrl);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var sessionId = queryParams["SessionId"];
            var sst = queryParams["SST"];

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(sst))
                return (false, "Missing required parameters in payment URL", null, null, null);

            var initialResponse = await _http.GetAsync(paymentUrl);
            var initialContent = await initialResponse.Content.ReadAsStringAsync();

            var match = Regex.Match(initialContent, @"runLoopback\((.*?)\);", RegexOptions.Singleline);
            if (!match.Success)
                return (false, "ADF loopback script not found", null, null, null);

            var afrLoopMatch = Regex.Match(initialContent, @"'_afrLoop',\s*'(\d+)'");
            var windowIdMatch = Regex.Match(initialContent,
              @"'Adf-Window-Id'\s*,\s*'_afrPage'\s*,\s*''\s*,\s*'([^']+)'",
              RegexOptions.Singleline);

            if (!afrLoopMatch.Success || !windowIdMatch.Success)
                return (false, "Missing ADF parameters in script", null, null, null);

            var afrLoop = afrLoopMatch.Groups[1].Value;
            var afdWindowId = windowIdMatch.Groups[1].Value;

            var loopbackUrl = $"{uri.Scheme}://{uri.Host}/AsicPayment/faces/index.jspx?SessionId={sessionId}&SST={sst}" +
                              $"&_afrLoop={afrLoop}&_afrWindowMode=2&Adf-Window-Id={afdWindowId}" +
                              "&_afrFS=16&_afrMT=screen&_afrMFW=1865&_afrMFH=926&_afrMFDW=1920&_afrMFDH=1080&_afrMFC=8&_afrMFCI=0&_afrMFM=0&_afrMFR=96&_afrMFG=0&_afrMFS=0&_afrMFO=0";

            var loopbackResponse = await _http.GetAsync(loopbackUrl);
            var content = await loopbackResponse.Content.ReadAsStringAsync();

            var document = await _htmlParser.ParseDocumentAsync(content);

            var viewState = document.QuerySelector("input[name='javax.faces.ViewState']")?.GetAttribute("value");

            var paymentUrlSpan = document.QuerySelector("span[title='hiddenpaymenturl']");
            if (paymentUrlSpan == null)
                return (false, "Could not find tokenization form URL", null, null, null);

            return (true, "Success", paymentUrlSpan.TextContent.Trim(), afdWindowId, viewState);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null, null, null);
        }
    }

    public async Task<PaymentResult> CompletePaymentAsync(DeviceInfo deviceInfo, string paymentReturnUrl, string sessionId, string hostedTokenizationId, string adfWindowId, string viewState)
    {
        try
        {
            var deviceInfoXml = $"acceptHeader%22%3E%3Cs%3E{Uri.EscapeDataString(deviceInfo.AcceptHeader)}%3C%2Fs%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22colorDepth%22%3E%3Cn%3E{deviceInfo.ColorDepth}%3C%2Fn%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22screenHeight%22%3E%3Cn%3E{deviceInfo.ScreenHeight}%3C%2Fn%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22screenWidth%22%3E%3Cn%3E{deviceInfo.ScreenWidth}%3C%2Fn%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22locale%22%3E%3Cs%3E{deviceInfo.Locale}%3C%2Fs%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22javaEnabled%22%3E%3Cb%3E{(deviceInfo.JavaEnabled ? "1" : "0")}%3C%2Fb%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22timezoneOffsetUtcMinutes%22%3E%3Cn%3E{deviceInfo.TimezoneOffsetUtcMinutes}%3C%2Fn%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22ipAddress%22%3E%3Cs%3E{deviceInfo.IpAddress}%3C%2Fs%3E%3C%2Fk%3E" +
                              $"%3Ck+v%3D%22userAgent%22%3E%3Cs%3E{Uri.EscapeDataString(deviceInfo.UserAgent)}%3C%2Fs%3E%3C%2Fk%3E";

            var formData = new StringBuilder();
            formData.Append("org.apache.myfaces.trinidad.faces.FORM=frmASICPayment&");
            formData.Append($"Adf-Window-Id={adfWindowId}&");
            formData.Append("Adf-Page-Id=3&");
            formData.Append($"javax.faces.ViewState={viewState}&");
            formData.Append("event=r1%3A0%3AsubmitBtn&");
            formData.Append($"event.r1:0:submitBtn=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22_custom%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22{deviceInfoXml}%3Ck+v%3D%22immediate%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EinvokeJavaMethod%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
            formData.Append("oracle.adf.view.rich.PROCESS=r1%3A0%3AsubmitBtn");

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?Adf-Window-Id={adfWindowId}&Adf-Page-Id=3");


            var formBytes = Encoding.UTF8.GetBytes(formData.ToString());
            request.Content = new ByteArrayContent(formBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            request.Headers.Add("Adf-Rich-Message", "true");
            request.Headers.Add("Adf-Ads-Page-Id", "5");
            request.Headers.Add("Origin", "https://regpayment.asic.gov.au");
            request.Headers.TryAddWithoutValidation("Referer", $"https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SST={hostedTokenizationId}&SessionId={sessionId}");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            // Step 1: POST to payment gateway to close the window (Adf-Window-Unloaded)
            var closeWindowRequest = new HttpRequestMessage(HttpMethod.Post, "https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx");
            var closeWindowContent = new StringBuilder();
            closeWindowContent.Append($"Adf-Window-Id={adfWindowId}&");
            closeWindowContent.Append("Adf-Page-Id=0");

            closeWindowRequest.Content = new StringContent(closeWindowContent.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            closeWindowRequest.Headers.Add("Adf-Rich-Message", "true");
            closeWindowRequest.Headers.Add("Adf-Window-Unloaded", "true");

            var closeResponse = await _http.SendAsync(closeWindowRequest);
            // Response should be: <?xml version="1.0" ?><partial-response><noop/></partial-response>

            // Step 2: GET the payment success callback page
            var successUrl = $"https://asicconnect.asic.gov.au/public/paymentSuccess.jsp?SessionId={sessionId}&SST={hostedTokenizationId}";
            var successResponse = await _http.GetAsync(successUrl);
            var successContent = await successResponse.Content.ReadAsStringAsync();

            if (!successResponse.IsSuccessStatusCode || !successContent.Contains("parent.paymentSuccess"))
            {
                return PaymentResult.Failed("Failed to retrieve payment success callback");
            }

            // Step 3: POST the payment success action back to the renewal page
            var paymentSuccessRequest = new HttpRequestMessage(HttpMethod.Post,
                $"https://asicconnect.asic.gov.au/public/faces/renewal?Adf-Window-Id={adfWindowId}&Adf-Page-Id=1");

            var paymentSuccessData = new StringBuilder();
            paymentSuccessData.Append("tmpt:connectHeaderView:searchWithinDropDown=&");
            paymentSuccessData.Append("tmpt:connectHeaderView:searchForNeedle=&");
            paymentSuccessData.Append("tmpt:connectHeaderView:searchForNeedle2=&");
            paymentSuccessData.Append("tmpt:connectHeaderView:searchForNeedle3=&");
            paymentSuccessData.Append($"popt=tmpt%3Aregion%3A3%3ApayNow&");
            paymentSuccessData.Append("org.apache.myfaces.trinidad.faces.FORM=tmpt%3Aform&");
            paymentSuccessData.Append($"Adf-Window-Id={adfWindowId}&");
            paymentSuccessData.Append("Adf-Page-Id=0&");
            paymentSuccessData.Append($"javax.faces.ViewState={viewState}&");
            paymentSuccessData.Append($"oracle.adf.view.rich.DELTAS=%7Btmpt%3Aregion%3A3%3ApayNowPopup%3D%7B_shown%3D%7D%2Ctmpt%3Aregion%3A3%3ApayNowInline%3D%7Bsource%3D%7D%7D&");
            paymentSuccessData.Append($"event=tmpt%3Aregion%3A3%3ApayNowPopup&");
            paymentSuccessData.Append($"event.tmpt:region:3:payNowPopup=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22_custom%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22sessionId%22%3E%3Cs%3E{sessionId}%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22SST%22%3E%3Cs%3E{hostedTokenizationId}%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22immediate%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EpaymentSuccessAction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
            paymentSuccessData.Append($"oracle.adf.view.rich.PROCESS=tmpt%3Aregion%3A3%3ApayNowPopup");

            paymentSuccessRequest.Content = new StringContent(paymentSuccessData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            paymentSuccessRequest.Headers.Add("Adf-Rich-Message", "true");
            paymentSuccessRequest.Headers.Add("Adf-Ads-Page-Id", "1");

            var finalResponse = await _http.SendAsync(paymentSuccessRequest);
            var finalContent = await finalResponse.Content.ReadAsStringAsync();

            // Check for errors in the response
            if (finalContent.Contains("declined") || finalContent.Contains("Your transaction has been declined"))
            {
                return PaymentResult.Failed("Payment was declined by your financial institution");
            }

            if (finalContent.Contains("error") || finalContent.Contains("<html>") && finalContent.Contains("<p>"))
            {
                // Extract error message from HTML
                var errorMatch = Regex.Match(finalContent, @"<p>(.*?)</p>", RegexOptions.Singleline);
                var errorMessage = errorMatch.Success ? errorMatch.Groups[1].Value.Trim() : "Payment error occurred";
                return PaymentResult.Failed(errorMessage);
            }

            // Success - check for confirmation
            if (finalResponse.IsSuccessStatusCode && !finalContent.Contains("declined") && !finalContent.Contains("error"))
            {
                return PaymentResult.Succeeded(sessionId);
            }

            return PaymentResult.Failed("Unknown payment status");
        }
        catch (Exception ex)
        {
            return PaymentResult.Failed($"Payment completion failed: {ex.Message}");
        }
    }
  
    private string ExtractTokenFromUrl(string url)
    {
        // Extract the last part of the URL path (the token)
        var uri = new Uri(url);
        var segments = uri.Segments;
        return segments[segments.Length - 1];
    }

    private DeviceInfo CreateDeviceInfo()
    {
        return new DeviceInfo
        {
            AcceptHeader = "en-US",
            ColorDepth = 24,
            ScreenHeight = 1080,
            ScreenWidth = 1920,
            Locale = "en-US",
            JavaEnabled = false,
            TimezoneOffsetUtcMinutes = -180,
            IpAddress = "0.0.0.0",
            UserAgent = UserAgent
        };
    }
}