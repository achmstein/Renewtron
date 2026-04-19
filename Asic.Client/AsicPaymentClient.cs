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
    private readonly ISmsProvider _smsProvider;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0";

    public AsicPaymentClient(ISmsProvider smsProvider)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

        _htmlParser = new HtmlParser();
        _smsProvider = smsProvider;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, CreditCardDetails cardDetails)
    {
        try
        {
            var initialData = PaymentStepData.Create(paymentUrl, sessionId, cardDetails);

            // Railway-oriented pipeline: each step only executes if the previous step succeeded
            // After SubmitDeviceInfo, we handle the methodurl.psp-solutions.com orchestrator flow
            // which redirects to the RSA 3DS challenge.
            return await InitializeSessionStepAsync(initialData)
                .ThenAsync("Load Tokenization Form", LoadTokenizationFormStepAsync)
                .ThenAsync("Get Card Information", GetCardInformationStepAsync)
                .ThenAsync("Prepare 3DS", PrepareThreeDSStepAsync)
                .ThenAsync("Submit Tokenization", SubmitTokenizationStepAsync)
                .ThenAsync("Submit Device Information", SubmitDeviceInformationStepAsync)
                .ThenAsync("Handle 3DS Method Orchestrator", Handle3DSMethodOrchestratorStepAsync)
                .ThenAsync("Submit 3DS Challenge", Submit3DSChallengeStepAsync)
                .ThenAsync("Submit OTP", SubmitOtpStepAsync)
                .ThenAsync("Close Payment Window", ClosePaymentWindowStepAsync)
                .ToPaymentResultAsync(data =>
                {
                    if (data.ThreeDSRequiresOtp)
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

            data.ThreeDSServerTransactionId = json["threeDSServerTransactionId"]?.ToString();
            data.ThreeDSMethodUrl = json["methodUrl"]?.ToString();
            data.ThreeDSMessageVersion = json["version"]?.ToString() ?? "2.2.0";

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Prepare 3DS");
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
            formData.Append("hasMethodUrlSucceeded=true&");
            formData.Append($"threeDSServerTransactionId={data.ThreeDSServerTransactionId}&");
            formData.Append($"cardnumber={Regex.Replace(data.CardDetails.CardNumber, ".{4}", "$0 ").TrimEnd()}&");
            formData.Append("browserColorDepth=24&");
            formData.Append("browserJavaEnabled=false&");
            formData.Append("browserLanguage=en-US&");
            formData.Append("browserScreenHeight=1080&");
            formData.Append("browserScreenWidth=1920&");
            formData.Append("browserTimeZone=-120&");
            formData.Append("cobadging=&");
            formData.Append("cobadging-indicator=default&");
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

            // Frictionless 3DS: the issuer skipped the challenge and the gateway jumped straight
            // to asicconnect.asic.gov.au/public/paymentSuccess.jsp (or paymentFailure.jsp).
            // Short-circuit the OTP/challenge pipeline — payment has already been authorised/declined.
            if (data.ThreeDSRedirectUrl.Contains("/paymentSuccess.jsp", StringComparison.OrdinalIgnoreCase))
            {
                data.ThreeDSComplete = true;
                data.ThreeDSRequiresOtp = false;
                data.ThreeDSProvider = "Frictionless";
                return StepResult<PaymentStepData>.Success(data);
            }

            if (data.ThreeDSRedirectUrl.Contains("/paymentFailure.jsp", StringComparison.OrdinalIgnoreCase))
            {
                return StepResult<PaymentStepData>.Failure("Payment was declined by the issuer", "Submit Device Information");
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit Device Information");
        }
    }

    private async Task<StepResult<PaymentStepData>> Handle3DSMethodOrchestratorStepAsync(PaymentStepData data)
    {
        // Frictionless path: no orchestrator/challenge to handle
        if (data.ThreeDSComplete)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            // ThreeDSRedirectUrl now points to payment.anzworldline-solutions.com.au/v1/redirect/handlerequest/{guid}
            // This returns an HTML page with auto-submit form to Cardinal Commerce

            // Step 1: GET the redirect handler page
            var request = new HttpRequestMessage(HttpMethod.Get, data.ThreeDSRedirectUrl);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "document");
            request.Headers.Add("Referer", "https://regpayment.asic.gov.au/");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Handle 3DS Method Orchestrator");

            // Step 2: Parse the auto-submit form that redirects to Cardinal Commerce
            var doc = await _htmlParser.ParseDocumentAsync(content);
            var form = doc.QuerySelector("form#redirect") ?? doc.QuerySelector("form");

            if (form == null)
                return StepResult<PaymentStepData>.Failure("Could not find redirect form", "Handle 3DS Method Orchestrator");

            var formAction = form.GetAttribute("action");
            var creq = form.QuerySelector("[name='creq']")?.GetAttribute("value");
            var threeDSSessionData = form.QuerySelector("[name='threeDSSessionData']")?.GetAttribute("value");

            if (string.IsNullOrWhiteSpace(formAction) || string.IsNullOrWhiteSpace(creq))
                return StepResult<PaymentStepData>.Failure("Could not extract Cardinal Commerce form data", "Handle 3DS Method Orchestrator");

            data.ThreeDSChallengeUrl = HttpUtility.HtmlDecode(formAction);
            data.ThreeDSCReq = creq;
            data.ThreeDSSessionData = threeDSSessionData;

            var challengeUri = new Uri(data.ThreeDSChallengeUrl);
            var queryParams = HttpUtility.ParseQueryString(challengeUri.Query);

            if (challengeUri.Host.Contains("rsa3dsauth", StringComparison.OrdinalIgnoreCase))
            {
                // RSA 3DS (e.g. NAB / ANZ Worldline)
                // Format: https://www.rsa3dsauth.co.uk/3ds2/cReqWebBased?issuer=<issuer>
                data.ThreeDSProvider = "Rsa";
                data.RsaIssuer = queryParams["issuer"];
                data.ThreeDSIssuerId = data.RsaIssuer;
            }
            else
            {
                // Cardinal Commerce (default)
                // Format: https://authentication.cardinalcommerce.com/ThreeDSecure/V2_1_0/CReq?oid={oid}&tid={tid}
                data.ThreeDSProvider = "Cardinal";
                data.CardinalOid = queryParams["oid"];
                data.CardinalTid = queryParams["tid"];
                data.ThreeDSIssuerId = data.CardinalOid;
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Handle 3DS Method Orchestrator");
        }
    }

    private async Task<StepResult<PaymentStepData>> Submit3DSChallengeStepAsync(PaymentStepData data)
    {
        // Frictionless path: no challenge to submit
        if (data.ThreeDSComplete)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            // POST to Cardinal Commerce CReq endpoint
            var request = new HttpRequestMessage(HttpMethod.Post, data.ThreeDSChallengeUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "creq", data.ThreeDSCReq },
                    { "threeDSSessionData", data.ThreeDSSessionData ?? "" }
                })
            };

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            request.Headers.Add("Origin", "https://payment.anzworldline-solutions.com.au");
            request.Headers.Add("Referer", "https://payment.anzworldline-solutions.com.au/");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");
            request.Headers.Add("Sec-Fetch-Storage-Access", "none");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Challenge submission failed: {response.StatusCode}", "Submit 3DS Challenge");

            // Store the response for OTP extraction
            data.ThreeDSChallengeResponseContent = content;
            data.ThreeDSRequiresOtp = true;

            if (data.ThreeDSProvider == "Cardinal")
            {
                // Cardinal Commerce uses acsTransID from the creq JWT (tid parameter)
                data.ThreeDSAcsTransId = data.CardinalTid;
                data.CardinalLanguageCode = "en-us";
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit 3DS Challenge");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitOtpStepAsync(PaymentStepData data)
    {
        // Skip if OTP not required
        if (!data.ThreeDSRequiresOtp)
            return StepResult<PaymentStepData>.Success(data);

        if (data.ThreeDSProvider == "Rsa")
            return await SubmitRsaOtpStepAsync(data);

        try
        {
            const string cardinalBaseUrl = "https://authentication.cardinalcommerce.com";

            // Step 1: Choose credential (select SMS OTP option)
            var chooseCredentialUrl = $"{cardinalBaseUrl}/Api/2_1_0/NextStep/ChooseCredential";
            var chooseCredentialData = new Dictionary<string, string>
            {
                ["CredentialId"] = "a",
                ["TransactionId"] = data.CardinalTid,
                ["ChoiceTimeout"] = "",
                ["IssuerId"] = data.CardinalOid,
                ["ChoiceType"] = "Grouped",
                ["X-Requested-With"] = "XMLHttpRequest",
                ["X-HTTP-Method-Override"] = "FORM"
            };

            var chooseRequest = new HttpRequestMessage(HttpMethod.Post, chooseCredentialUrl)
            {
                Content = new FormUrlEncodedContent(chooseCredentialData)
            };
            chooseRequest.Headers.Add("Accept", "*/*");
            chooseRequest.Headers.Add("Origin", cardinalBaseUrl);
            chooseRequest.Headers.Add("Referer", data.ThreeDSChallengeUrl);
            chooseRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

            var chooseResponse = await _http.SendAsync(chooseRequest);
            var chooseContent = await chooseResponse.Content.ReadAsStringAsync();

            if (!chooseResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"ChooseCredential failed: {chooseResponse.StatusCode}", "Submit OTP");

            // Parse response to get next step info
            var chooseJson = JsonNode.Parse(chooseContent)?.AsObject();
            var nextStep = chooseJson?["NextStep"]?.ToString();

            if (nextStep != "VALIDATE")
                return StepResult<PaymentStepData>.Failure($"Unexpected NextStep: {nextStep}", "Submit OTP");

            // Step 2: Navigate to VALIDATE page to trigger OTP send
            var validatePageUrl = $"{cardinalBaseUrl}/2_1_0/VALIDATE/{data.CardinalOid}";
            var validatePageData = new Dictionary<string, string>
            {
                ["TransactionId"] = data.CardinalTid,
                ["GroupId"] = "",
                ["Type"] = "",
                ["LanguageCode"] = "en-us",
                ["CardBrand"] = data.BrandName ?? "Visa",
                ["Content.TemplateName"] = "Choice_OneUpOneDown_GroupedList_FullWidth",
                ["Content.WorkflowScreenName"] = "choice",
                ["ChoiceType"] = "Grouped",
                ["IssuerId"] = data.CardinalOid
            };

            var validatePageRequest = new HttpRequestMessage(HttpMethod.Post, validatePageUrl)
            {
                Content = new FormUrlEncodedContent(validatePageData)
            };
            validatePageRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            validatePageRequest.Headers.Add("Origin", cardinalBaseUrl);
            validatePageRequest.Headers.Add("Referer", data.ThreeDSChallengeUrl);

            var validatePageResponse = await _http.SendAsync(validatePageRequest);
            var validatePageContent = await validatePageResponse.Content.ReadAsStringAsync();

            if (!validatePageResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Validate page failed: {validatePageResponse.StatusCode}", "Submit OTP");

            // Extract StepupRequestId from the validate page response
            var stepupRequestIdMatch = Regex.Match(validatePageContent, @"StepupRequestId[""']?\s*[:=]\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (stepupRequestIdMatch.Success)
            {
                data.CardinalStepupRequestId = stepupRequestIdMatch.Groups[1].Value;
            }

            // Also try to extract from hidden input
            if (string.IsNullOrEmpty(data.CardinalStepupRequestId))
            {
                var doc = await _htmlParser.ParseDocumentAsync(validatePageContent);
                data.CardinalStepupRequestId = doc.QuerySelector("input[name='StepupRequestId']")?.GetAttribute("value");
            }

            // Step 3: Wait for OTP from SMS provider
            string otp;
            try
            {
                otp = await _smsProvider.GetOtpAsync();
            }
            catch (Exception ex)
            {
                return StepResult<PaymentStepData>.Failure($"Failed to retrieve OTP: {ex.Message}", "Submit OTP");
            }

            // Step 4: Submit OTP via ValidateCredential API
            var validateCredentialUrl = $"{cardinalBaseUrl}/Api/2_1_0/NextStep/ValidateCredential";
            var validateCredentialData = new Dictionary<string, string>
            {
                ["TransactionId"] = data.CardinalTid,
                ["StepupRequestId"] = data.CardinalStepupRequestId ?? "",
                ["ValidateTimeout"] = "",
                ["IssuerId"] = data.CardinalOid,
                ["ValidateReloadEnabled"] = "True",
                ["VisaBehavioralAnalyticsEnabled"] = "False",
                ["LanguageCode"] = "en-us",
                ["LanguageShortCode"] = "en",
                ["CredentialValidationMessage"] = "Please re-enter your code",
                ["Credential.Value"] = otp,
                ["Credential.Id"] = "a",
                ["X-Requested-With"] = "XMLHttpRequest",
                ["X-HTTP-Method-Override"] = "FORM"
            };

            var validateRequest = new HttpRequestMessage(HttpMethod.Post, validateCredentialUrl)
            {
                Content = new FormUrlEncodedContent(validateCredentialData)
            };
            validateRequest.Headers.Add("Accept", "*/*");
            validateRequest.Headers.Add("Origin", cardinalBaseUrl);
            validateRequest.Headers.Add("Referer", validatePageUrl);
            validateRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

            var validateResponse = await _http.SendAsync(validateRequest);
            var validateContent = await validateResponse.Content.ReadAsStringAsync();

            if (!validateResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"ValidateCredential failed: {validateResponse.StatusCode}", "Submit OTP");

            // Check response for success or error
            var validateJson = JsonNode.Parse(validateContent)?.AsObject();
            var message = validateJson?["Message"]?.AsObject();
            var messageContent = message?["Content"]?.ToString();

            if (messageContent?.Contains("re-enter your code", StringComparison.OrdinalIgnoreCase) == true ||
                messageContent?.Contains("incorrect", StringComparison.OrdinalIgnoreCase) == true)
            {
                return StepResult<PaymentStepData>.Failure("Invalid OTP code", "Submit OTP");
            }

            // Check if we need to follow up with final result
            var responseNextStep = validateJson?["NextStep"]?.ToString();
            if (responseNextStep == "COMPLETE" || responseNextStep == "RESULT")
            {
                data.ThreeDSComplete = true;
                data.ThreeDSRequiresOtp = false;
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit OTP");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitRsaOtpStepAsync(PaymentStepData data)
    {
        try
        {
            // Step 1: Parse the OTP entry form (<form id="mainForm">) that was returned by the cReqWebBased POST
            var challengeDoc = await _htmlParser.ParseDocumentAsync(data.ThreeDSChallengeResponseContent ?? "");
            var mainForm = challengeDoc.QuerySelector("form#mainForm") ?? challengeDoc.QuerySelector("form");

            if (mainForm == null)
                return StepResult<PaymentStepData>.Failure("Could not find RSA OTP entry form", "Submit OTP");

            var challengeWindowSize = mainForm.QuerySelector("input[name='challengeWindowSize']")?.GetAttribute("value") ?? "01";
            var threeDSServerTransID = mainForm.QuerySelector("input[name='threeDSServerTransID']")?.GetAttribute("value");
            var messageVersion = mainForm.QuerySelector("input[name='messageVersion']")?.GetAttribute("value") ?? data.ThreeDSMessageVersion ?? "2.2.0";
            var acsTransID = mainForm.QuerySelector("input[name='acsTransID']")?.GetAttribute("value");
            var submitAction = HttpUtility.HtmlDecode(mainForm.GetAttribute("action") ?? "");

            if (string.IsNullOrWhiteSpace(submitAction) || string.IsNullOrWhiteSpace(threeDSServerTransID) || string.IsNullOrWhiteSpace(acsTransID))
                return StepResult<PaymentStepData>.Failure("RSA form missing required fields", "Submit OTP");

            data.ThreeDSAcsTransId = acsTransID;
            data.ThreeDSTransactionId = threeDSServerTransID;
            data.ThreeDSMessageVersion = messageVersion;
            data.ThreeDSChallengeWindowSize = challengeWindowSize;

            // Resolve submit action to absolute URL (action may be relative or absolute)
            var submitUri = Uri.IsWellFormedUriString(submitAction, UriKind.Absolute)
                ? new Uri(submitAction)
                : new Uri(new Uri(data.ThreeDSChallengeUrl), submitAction);

            // Step 2: Wait for OTP from SMS provider
            string otp;
            try
            {
                otp = await _smsProvider.GetOtpAsync();
            }
            catch (Exception ex)
            {
                return StepResult<PaymentStepData>.Failure($"Failed to retrieve OTP: {ex.Message}", "Submit OTP");
            }

            // Step 3: POST OTP to challengeSubmit
            var submitRequest = new HttpRequestMessage(HttpMethod.Post, submitUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["challengeWindowSize"] = challengeWindowSize,
                    ["threeDSServerTransID"] = threeDSServerTransID,
                    ["messageVersion"] = messageVersion,
                    ["acsTransID"] = acsTransID,
                    ["dataEntry"] = otp
                })
            };
            submitRequest.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            submitRequest.Headers.TryAddWithoutValidation("Origin", $"{submitUri.Scheme}://{submitUri.Host}");
            submitRequest.Headers.TryAddWithoutValidation("Referer", data.ThreeDSChallengeUrl);
            submitRequest.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            submitRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            submitRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            submitRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "iframe");

            var submitResponse = await _http.SendAsync(submitRequest);
            var submitContent = await submitResponse.Content.ReadAsStringAsync();

            if (!submitResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"RSA challengeSubmit failed: {submitResponse.StatusCode}", "Submit OTP");

            // If the server returned the same OTP form again, the code was rejected
            if (submitContent.Contains("challengeSubmit", StringComparison.OrdinalIgnoreCase) &&
                submitContent.Contains("dataEntry", StringComparison.OrdinalIgnoreCase) &&
                !submitContent.Contains("cres_submit", StringComparison.OrdinalIgnoreCase) &&
                !submitContent.Contains("name=\"cres\"", StringComparison.OrdinalIgnoreCase))
            {
                return StepResult<PaymentStepData>.Failure("Invalid OTP code", "Submit OTP");
            }

            // Step 4: Parse the returned <form id="cres_submit"> and auto-POST cres+threeDSSessionData back to ANZ Worldline
            var cresDoc = await _htmlParser.ParseDocumentAsync(submitContent);
            var cresForm = cresDoc.QuerySelector("form#cres_submit") ?? cresDoc.QuerySelector("form");

            if (cresForm == null)
                return StepResult<PaymentStepData>.Failure("Could not find cres_submit form after OTP", "Submit OTP");

            var cresAction = HttpUtility.HtmlDecode(cresForm.GetAttribute("action") ?? "");
            var cresValue = cresForm.QuerySelector("input[name='cres']")?.GetAttribute("value");
            var cresSessionData = cresForm.QuerySelector("input[name='threeDSSessionData']")?.GetAttribute("value");

            if (string.IsNullOrWhiteSpace(cresAction) || string.IsNullOrWhiteSpace(cresValue))
                return StepResult<PaymentStepData>.Failure("cres form missing required fields", "Submit OTP");

            var cresUri = Uri.IsWellFormedUriString(cresAction, UriKind.Absolute)
                ? new Uri(cresAction)
                : new Uri(submitUri, cresAction);

            var finalForm = new Dictionary<string, string> { ["cres"] = cresValue };
            if (!string.IsNullOrWhiteSpace(cresSessionData))
                finalForm["threeDSSessionData"] = cresSessionData;

            var finalRequest = new HttpRequestMessage(HttpMethod.Post, cresUri) { Content = new FormUrlEncodedContent(finalForm) };
            finalRequest.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            finalRequest.Headers.TryAddWithoutValidation("Origin", $"{submitUri.Scheme}://{submitUri.Host}");
            finalRequest.Headers.TryAddWithoutValidation("Referer", submitUri.ToString());
            finalRequest.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            var finalResponse = await _http.SendAsync(finalRequest);

            if (!finalResponse.IsSuccessStatusCode && finalResponse.StatusCode != HttpStatusCode.Found && finalResponse.StatusCode != HttpStatusCode.Redirect)
                return StepResult<PaymentStepData>.Failure($"cres submission failed: {finalResponse.StatusCode}", "Submit OTP");

            data.ThreeDSComplete = true;
            data.ThreeDSRequiresOtp = false;

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