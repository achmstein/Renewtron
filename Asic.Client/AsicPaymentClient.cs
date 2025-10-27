using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using Polly;
using Polly.Retry;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    public async Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, CreditCardDetails cardDetails)
    {
        try
        {
            var initialData = PaymentStepData.Create(paymentUrl, sessionId, cardDetails);

            // Railway-oriented pipeline: each step only executes if the previous step succeeded
            return await InitializeSessionStepAsync(initialData)
                .ThenAsync("Load Tokenization Form", LoadTokenizationFormStepAsync)
                .ThenAsync("Get Card Information", GetCardInformationStepAsync)
                .ThenAsync("Prepare 3DS", PrepareThreeDSStepAsync)
                .ThenAsync("Submit Initial Payment", SubmitInitialPaymentStepAsync)
                .ThenAsync("Submit Tokenization", SubmitTokenizationStepAsync)
                .ThenAsync("Submit Device Information", SubmitDeviceInformationStepAsync)
                .ThenAsync("Get 3DS Iframe", Get3DSIframeStepAsync)
                .ThenAsync("Extract 3DS Form Data", Extract3DSFormDataStepAsync)
                .ThenAsync("Submit To CardinalCommerce", SubmitToCardinalCommerceStepAsync)
                .ThenAsync("Process 3DS Callback", Process3DSCallbackStepAsync)
                .ThenAsync("Check 3DS Challenge", Check3DSChallengeStepAsync)
                .ThenAsync("Handle 3DS Challenge", Handle3DSChallengeStepAsync)
                .ThenAsync("Submit OTP", SubmitOTPStepAsync)
                .ThenAsync("Close Payment Window", ClosePaymentWindowStepAsync)
                .ToPaymentResultAsync(data =>
                {
                    if (data.ThreeDSRequiresOTP)
                    {
                        return PaymentResult.Failed(
                            $"3DS Challenge requires OTP. Transaction ID: {data.ThreeDSTransactionId}, " +
                            $"Issuer ID: {data.ThreeDSIssuerId}, Challenge URL: {data.ThreeDSChallengeUrl}");
                    }
                    return PaymentResult.Succeeded(data.HostedTokenizationId);
                });
        }
        catch (Exception ex)
        {
            return PaymentResult.Failed($"Payment processing failed: {ex.Message}");
        }
    }

    private async Task<StepResult<PaymentStepData>> InitializeSessionStepAsync(PaymentStepData data)
    {
        try
        {
            var uri = new Uri(data.PaymentUrl);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var sessionId = queryParams["SessionId"];
            var sst = queryParams["SST"];

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(sst))
            {
                return StepResult<PaymentStepData>.Failure("Missing required parameters in payment URL", "Initialize Session");
            }

            var initialResponse = await _http.GetAsync(data.PaymentUrl);
            var initialContent = await initialResponse.Content.ReadAsStringAsync();

            var match = Regex.Match(initialContent, @"runLoopback\((.*?)\);", RegexOptions.Singleline);
            if (!match.Success)
            {
                return StepResult<PaymentStepData>.Failure("ADF loopback script not found", "Initialize Session");
            }

            var afrLoopMatch = Regex.Match(initialContent, @"'_afrLoop',\s*'(\d+)'");
            var windowIdMatch = Regex.Match(initialContent,
              @"'Adf-Window-Id'\s*,\s*'_afrPage'\s*,\s*''\s*,\s*'([^']+)'",
              RegexOptions.Singleline);

            if (!afrLoopMatch.Success || !windowIdMatch.Success)
            {
                return StepResult<PaymentStepData>.Failure("Missing ADF parameters in script", "Initialize Session");
            }

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
            {
                return StepResult<PaymentStepData>.Failure("Could not find tokenization form URL", "Initialize Session");
            }

            data.TokenizationFormUrl = paymentUrlSpan.TextContent.Trim();
            data.AdfWindowId = afdWindowId;
            data.ViewState = viewState;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Initialize Session");
        }
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
            var token = ExtractTokenFromUrl(data.TokenizationFormUrl);
            var binUrl = $"https://payment.anzworldline-solutions.com.au/hostedtokenization/bin/getcardinformation/{token}";

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

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(content).AsObject();

            data.ThreeDSServerTransactionId = json["threeDSServerTransactionId"].ToString();

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
            formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
            formData.Append($"javax.faces.ViewState={data.ViewState}&");
            formData.Append("Adf-Page-Id=3&");
            formData.Append("event=r1%3A0%3AsubmitBtn&");
            formData.Append("event.r1:0:submitBtn=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
            formData.Append("oracle.adf.view.rich.PROCESS=r1%2Cr1%3A0%3AsubmitBtn");

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=3");

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

            AsyncRetryPolicy<HttpResponseMessage> retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.RequestTimeout)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: _ => TimeSpan.FromSeconds(2),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        Console.WriteLine($"[Retry {retryAttempt}] Retrying after 2 seconds due to {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                    });

            var response = await retryPolicy.ExecuteAsync(() => _http.SendAsync(request));
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Submit Tokenization");
            }

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

    private async Task<StepResult<PaymentStepData>> SubmitDeviceInformationStepAsync(PaymentStepData data)
    {
        try
        {
            var deviceInfo = new DeviceInfo
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
            formData.Append($"Adf-Window-Id={data.AdfWindowId}&");
            formData.Append("Adf-Page-Id=1&");
            formData.Append($"javax.faces.ViewState={data.ViewState}&");
            formData.Append("event=r1%3A0%3AsubmitBtn&");
            formData.Append($"event.r1:0:submitBtn=%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22_custom%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22{deviceInfoXml}%3Ck+v%3D%22immediate%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EinvokeJavaMethod%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E&");
            formData.Append("oracle.adf.view.rich.PROCESS=r1%3A0%3AsubmitBtn");

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?Adf-Window-Id={data.AdfWindowId}&Adf-Page-Id=1");

            request.Content = new StringContent(formData.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            request.Headers.Add("Adf-Rich-Message", "true");
            request.Headers.Add("Adf-Ads-Page-Id", "2");
            request.Headers.Add("Origin", "https://regpayment.asic.gov.au");
            request.Headers.TryAddWithoutValidation("Referer", $"https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SST={data.HostedTokenizationId}&SessionId={data.SessionId}");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StepResult<PaymentStepData>.Failure("Failed to submit device information", "Submit Device Information");
            }

            var redirectUrlMatch = Regex.Match(content, @"window\.location\.href\s*=\s*'([^']+)'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!redirectUrlMatch.Success)
            {
                return StepResult<PaymentStepData>.Failure("Could not extract 3DS redirect URL", "Submit Device Information");
            }

            data.ThreeDSRedirectUrl = HttpUtility.HtmlDecode(redirectUrlMatch.Groups[1].Value);

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit Device Information");
        }
    }

    private async Task<StepResult<PaymentStepData>> Get3DSIframeStepAsync(PaymentStepData data)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, data.ThreeDSRedirectUrl);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Referer", "https://regpayment.asic.gov.au/");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var doc = await _htmlParser.ParseDocumentAsync(content);
            var iframeSrc = doc.QuerySelector("iframe")?.GetAttribute("src");

            if (string.IsNullOrWhiteSpace(iframeSrc))
                return StepResult<PaymentStepData>.Failure("Could not find 3DS iframe URL", "Get 3DS Iframe");

            data.ThreeDSIframeUrl = HttpUtility.HtmlDecode(iframeSrc);
            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Get 3DS Iframe");
        }
    }

    private async Task<StepResult<PaymentStepData>> Extract3DSFormDataStepAsync(PaymentStepData data)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, data.ThreeDSIframeUrl);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Referer", data.ThreeDSRedirectUrl);

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var doc = await _htmlParser.ParseDocumentAsync(content);
            var form = doc.QuerySelector("form");
            var formAction = form?.GetAttribute("action");
            var threeDSMethodData = form?.QuerySelector("[name='threeDSMethodData']")?.GetAttribute("value");

            if (string.IsNullOrWhiteSpace(formAction) || string.IsNullOrWhiteSpace(threeDSMethodData))
                return StepResult<PaymentStepData>.Failure("Could not extract 3DS form data", "Extract 3DS Form Data");

            data.ThreeDSFormAction = HttpUtility.HtmlDecode(formAction);
            data.ThreeDSMethodData = threeDSMethodData;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Extract 3DS Form Data");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitToCardinalCommerceStepAsync(PaymentStepData data)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, data.ThreeDSFormAction)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "threeDSMethodData", data.ThreeDSMethodData }
                })
            };

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Origin", new Uri(data.ThreeDSIframeUrl).GetLeftPart(UriPartial.Authority));
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Referer", data.ThreeDSIframeUrl);

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var doc = await _htmlParser.ParseDocumentAsync(content);
            var callbackUrl = doc?.QuerySelector("#notificationUrl")?.GetAttribute("value");
            var callbackData = doc?.QuerySelector("#base64payload")?.GetAttribute("value");

            // If no callback, 3DS is complete (frictionless flow)
            if (string.IsNullOrWhiteSpace(callbackUrl))
            {
                data.ThreeDSComplete = true;
                return StepResult<PaymentStepData>.Success(data);
            }

            data.ThreeDSCallbackUrl = callbackUrl;
            data.ThreeDSCallbackData = callbackData;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit To CardinalCommerce");
        }
    }

    private async Task<StepResult<PaymentStepData>> Process3DSCallbackStepAsync(PaymentStepData data)
    {
        // Skip if 3DS already complete (frictionless flow)
        if (data.ThreeDSComplete)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, data.ThreeDSCallbackUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "threeDSMethodData", data.ThreeDSCallbackData }
                })
            };

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Origin", "https://geoissuer.cardinalcommerce.com");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Referer", "https://geoissuer.cardinalcommerce.com/");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var doc = await _htmlParser.ParseDocumentAsync(content);
            var script = doc.Scripts.FirstOrDefault(s => s.TextContent.Contains("window.open"));
            var resultUrl = Regex.Match(script?.TextContent ?? "", @"window\.open\(['""]([^'""]+)['""]").Groups[1].Value;

            if (string.IsNullOrWhiteSpace(resultUrl))
                return StepResult<PaymentStepData>.Failure("Could not find result redirect URL", "Process 3DS Callback");

            data.ThreeDSResultUrl = resultUrl;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Process 3DS Callback");
        }
    }

    private async Task<StepResult<PaymentStepData>> Check3DSChallengeStepAsync(PaymentStepData data)
    {
        // Skip if 3DS already complete (frictionless flow)
        if (data.ThreeDSComplete)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, data.ThreeDSResultUrl);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Referer", data.ThreeDSCallbackUrl);

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            var doc = await _htmlParser.ParseDocumentAsync(content);
            var challengeForm = doc.QuerySelector("form");

            if (challengeForm == null)
            {
                // No challenge - frictionless success
                data.ThreeDSComplete = true;
                return StepResult<PaymentStepData>.Success(data);
            }

            var challengeUrl = HttpUtility.HtmlDecode(challengeForm.GetAttribute("action"));
            var threeDSSessionData = challengeForm.QuerySelector("[name='threeDSSessionData']")?.GetAttribute("value");
            var creq = challengeForm.QuerySelector("[name='creq']")?.GetAttribute("value");

            if (string.IsNullOrWhiteSpace(challengeUrl) || string.IsNullOrWhiteSpace(threeDSSessionData) || string.IsNullOrWhiteSpace(creq))
                return StepResult<PaymentStepData>.Failure("Could not extract challenge form data", "Check 3DS Challenge");

            data.ThreeDSChallengeUrl = challengeUrl;
            data.ThreeDSSessionData = threeDSSessionData;
            data.ThreeDSCReq = creq;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Check 3DS Challenge");
        }
    }

    private async Task<StepResult<PaymentStepData>> Handle3DSChallengeStepAsync(PaymentStepData data)
    {
        // Skip if no challenge needed
        if (data.ThreeDSComplete)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, data.ThreeDSChallengeUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "threeDSMethodData", data.ThreeDSSessionData },
                    { "creq", data.ThreeDSCReq }
                })
            };

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            request.Headers.Add("Origin", "https://methodurl.psp-solutions.com");
            request.Headers.Add("Referer", "https://methodurl.psp-solutions.com/");
            request.Headers.Add("DNT", "1");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Challenge submission failed: {response.StatusCode}", "Handle 3DS Challenge");

            // Check if OTP is required
            if (content.Contains("Your One-time Passcode has been sent"))
            {
                var doc = await _htmlParser.ParseDocumentAsync(content);
                var issuerId = doc.QuerySelector("#IssuerId")?.GetAttribute("value");
                var transactionId = doc.QuerySelector("#TransactionId")?.GetAttribute("value");

                if (string.IsNullOrWhiteSpace(issuerId) || string.IsNullOrWhiteSpace(transactionId))
                    return StepResult<PaymentStepData>.Failure("Could not extract OTP challenge data", "Handle 3DS Challenge");

                data.ThreeDSIssuerId = issuerId;
                data.ThreeDSTransactionId = transactionId;
                data.ThreeDSRequiresOTP = true;

                // Store the challenge response content for later OTP submission
                data.ThreeDSChallengeResponseContent = content;
            }
            else
            {
                // No OTP required, challenge completed
                data.ThreeDSComplete = true;
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Handle 3DS Challenge");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitOTPStepAsync(PaymentStepData data)
    {
        // Skip if OTP not required
        if (!data.ThreeDSRequiresOTP)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            //This step would need to be implemented based on how you want to handle OTP
            //For now, we just pass through and let the final result handler report OTP is needed

            //If you have an OTP provider / callback, you would:
            // 1.Get the OTP(from user input, SMS service, etc.)
            // 2.Submit it using the code below

            // Example implementation(commented out):

            var otp = "123456"; // Implement this based on your needs

            var postData = new Dictionary<string, string>
            {
                ["LanguageCode"] = "en-us",
                ["LanguageShortCode"] = "en",
                ["CredentialValidationMessage"] = "Please re-enter your code",
                ["Credential.Value"] = otp,
                ["Credential.Id"] = "a",
                ["TransactionId"] = data.ThreeDSTransactionId,
                ["ValidateTimeout"] = "",
                ["IssuerId"] = data.ThreeDSIssuerId,
                ["OtpResendTimeout"] = "0",
                ["ValidateReloadEnabled"] = "True",
                ["X-Requested-With"] = "XMLHttpRequest",
                ["X-HTTP-Method-Override"] = "FORM"
            };

            var validateUrl = "https://authentication.cardinalcommerce.com/Api/2_1_0/NextStep/ValidateCredential";
            var validateRequest = new HttpRequestMessage(HttpMethod.Post, validateUrl)
            {
                Content = new FormUrlEncodedContent(postData)
            };

            validateRequest.Headers.Add("Accept", "application/json, text/javascript, *; q=0.01");
            validateRequest.Headers.Referrer = new Uri(data.ThreeDSChallengeUrl);

            var validateResponse = await _http.SendAsync(validateRequest);
            var validateJson = await validateResponse.Content.ReadAsStringAsync();

            if (!validateResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"OTP validation failed: {validateJson}", "Submit OTP");

            using var jsonDoc = JsonDocument.Parse(validateJson);
            var nextStep = jsonDoc.RootElement.GetProperty("NextStep").GetString();

            if (!string.Equals(nextStep, "TERM", StringComparison.OrdinalIgnoreCase))
                return StepResult<PaymentStepData>.Failure($"Unexpected next step: {nextStep}", "Submit OTP");

            //  Submit TERM step
            var termData = new Dictionary<string, string>
            {
                ["TransactionId"] = data.ThreeDSTransactionId,
                ["IssuerId"] = data.ThreeDSIssuerId
            };

            var termUrl = "https://authentication.cardinalcommerce.com/api/2_1_0/nextstep/term";
            var termRequest = new HttpRequestMessage(HttpMethod.Post, termUrl)
            {
                Content = new FormUrlEncodedContent(termData)
            };
            termRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            termRequest.Headers.Add("Origin", "https://authentication.cardinalcommerce.com");
            termRequest.Headers.Referrer = new Uri(data.ThreeDSChallengeUrl);

            var termResponse = await _http.SendAsync(termRequest);
            var termJson = await termResponse.Content.ReadAsStringAsync();

            if (!termResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"TERM step failed: {termJson}", "Submit OTP");

            using var termDoc = JsonDocument.Parse(termJson);
            var payload = termDoc.RootElement.GetProperty("Payload");

            var cres = payload.GetProperty("CRes").GetString();
            var notificationUrl = payload.GetProperty("NotificationUrl").GetString();

            // Send CRes back to ACS Notification URL
            var finalResponse = await _http.PostAsync(notificationUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["cres"] = cres,
                    ["threeDSSessionData"] = data.ThreeDSSessionData,
                }));

            if (!finalResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Failed to send CRes: {finalResponse.StatusCode}", "Submit OTP");

            data.ThreeDSComplete = true;
            data.ThreeDSRequiresOTP = false;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit OTP");
        }
    }

    private async Task<StepResult<PaymentStepData>> ClosePaymentWindowStepAsync(PaymentStepData data)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx");
            var closeWindowContent = new StringBuilder();
            closeWindowContent.Append($"Adf-Window-Id={data.AdfWindowId}&");
            closeWindowContent.Append("Adf-Page-Id=0");

            request.Content = new StringContent(closeWindowContent.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            request.Headers.Add("Adf-Rich-Message", "true");
            request.Headers.Add("Adf-Window-Unloaded", "true");

            await _http.SendAsync(request);

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            // Window close is not critical, log but don't fail
            return StepResult<PaymentStepData>.Success(data);
        }
    }

    private string ExtractTokenFromUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.Segments;
        return segments[segments.Length - 1];
    }
}