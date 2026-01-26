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
            // Flow: PrepareThreeDS -> SecureSuite fingerprinting -> Tokenization -> DeviceInfo -> RSA 3DS Challenge
            return await InitializeSessionStepAsync(initialData)
                .ThenAsync("Load Tokenization Form", LoadTokenizationFormStepAsync)
                .ThenAsync("Get Card Information", GetCardInformationStepAsync)
                .ThenAsync("Prepare 3DS", PrepareThreeDSStepAsync)
                .ThenAsync("Handle SecureSuite 3DS Method", HandleSecureSuite3DSMethodStepAsync)
                .ThenAsync("Submit Tokenization", SubmitTokenizationStepAsync)
                .ThenAsync("Submit Device Information", SubmitDeviceInformationStepAsync)
                .ThenAsync("Handle 3DS Redirect", Handle3DSRedirectStepAsync)
                .ThenAsync("Submit 3DS Challenge", SubmitRsa3DSChallengeStepAsync)
                .ThenAsync("Submit OTP", SubmitRsaOtpStepAsync)
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

    private async Task<StepResult<PaymentStepData>> HandleSecureSuite3DSMethodStepAsync(PaymentStepData data)
    {
        try
        {
            // Skip if no method URL (3DS not required)
            if (string.IsNullOrEmpty(data.ThreeDSMethodUrl))
                return StepResult<PaymentStepData>.Success(data);

            var token = ExtractTokenFromUrl(data.TokenizationFormUrl);
            var callbackUrl = $"https://payment.anzworldline-solutions.com.au/hostedtokenization/ThreeDSecure/Callback/{token}";

            // Step 1: Create threeDSMethodData payload (base64 encoded JSON)
            var methodDataJson = JsonSerializer.Serialize(new
            {
                threeDSServerTransID = data.ThreeDSServerTransactionId,
                threeDSMethodNotificationURL = callbackUrl
            });
            var threeDSMethodData = Convert.ToBase64String(Encoding.UTF8.GetBytes(methodDataJson));

            // Step 2: POST to SecureSuite threeDSMethod endpoint
            var request = new HttpRequestMessage(HttpMethod.Post, data.ThreeDSMethodUrl);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "threeDSMethodData", threeDSMethodData }
            });
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Origin", "https://payment.anzworldline-solutions.com.au");
            request.Headers.Add("Referer", "https://payment.anzworldline-solutions.com.au/");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"SecureSuite 3DS Method failed: {response.StatusCode}", "Handle SecureSuite 3DS Method");

            // Step 3: Parse the form to get threeDSMethodSubmit URL and hidden fields
            var doc = await _htmlParser.ParseDocumentAsync(content);
            var form = doc.QuerySelector("form#theForm") ?? doc.QuerySelector("form");

            if (form == null)
                return StepResult<PaymentStepData>.Failure("Could not find SecureSuite form", "Handle SecureSuite 3DS Method");

            var formAction = form.GetAttribute("action");
            var threeDSServerTransID = form.QuerySelector("[name='threeDSServerTransID']")?.GetAttribute("value");
            var threeDSMethodNotificationURL = form.QuerySelector("[name='threeDSMethodNotificationURL']")?.GetAttribute("value");

            // Build the submit URL (relative to securesuite.co.uk)
            var methodUri = new Uri(data.ThreeDSMethodUrl);
            var submitUrl = formAction.StartsWith("http") ? formAction : $"{methodUri.Scheme}://{methodUri.Host}{formAction}";

            // Step 4: Submit to threeDSMethodSubmit with fingerprint data
            // We simulate the fingerprint data that would normally be collected by JS
            var submitData = new Dictionary<string, string>
            {
                { "threeDSServerTransID", threeDSServerTransID ?? data.ThreeDSServerTransactionId },
                { "threeDSMethodNotificationURL", threeDSMethodNotificationURL ?? callbackUrl },
                { "fingerprint", "" },
                { "html5_data", $"H.{Random.Shared.Next(10000000, 99999999)}.{Random.Shared.Next(1000000000, int.MaxValue)}" },
                { "clientHints", JsonSerializer.Serialize(new
                    {
                        architecture = "x86",
                        bitness = "64",
                        brands = new[] {
                            new { brand = "Not(A:Brand", version = "99" },
                            new { brand = "Microsoft Edge", version = "133" },
                            new { brand = "Chromium", version = "133" }
                        },
                        mobile = false,
                        platform = "Windows"
                    })
                },
                { "NF", "noflash" },
                { "FV", "noflash" },
                { "ERROR2", "5002" },
                { "page_timeout_flag", "false" },
                { "c_flash", "" },
                { "jsEnabled", "true" },
                { "cachedData", $"I.{Random.Shared.Next(10000000, 99999999)}.{Random.Shared.Next(1000000000, int.MaxValue)}" },
                { "cachedHeader", $"I.{Random.Shared.Next(10000000, 99999999)}.{Random.Shared.Next(1000000000, int.MaxValue)}" },
                { "orgIdIndicator", "" },
                { "wasmFingerprint", JsonSerializer.Serialize(new { wind = true, txt = Random.Shared.Next(10000000, 99999999).ToString(), geo = "402B4C8B" }) },
                { "wasmFPError", "" }
            };

            var submitRequest = new HttpRequestMessage(HttpMethod.Post, submitUrl);
            submitRequest.Content = new FormUrlEncodedContent(submitData);
            submitRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            submitRequest.Headers.Add("Origin", $"{methodUri.Scheme}://{methodUri.Host}");
            submitRequest.Headers.Add("Referer", data.ThreeDSMethodUrl);
            submitRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
            submitRequest.Headers.Add("Sec-Fetch-Mode", "navigate");
            submitRequest.Headers.Add("Sec-Fetch-Dest", "iframe");

            var submitResponse = await _http.SendAsync(submitRequest);
            var submitContent = await submitResponse.Content.ReadAsStringAsync();

            if (!submitResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"SecureSuite submit failed: {submitResponse.StatusCode}", "Handle SecureSuite 3DS Method");

            // Step 5: Parse response to get callback form
            var submitDoc = await _htmlParser.ParseDocumentAsync(submitContent);
            var callbackForm = submitDoc.QuerySelector("form#theForm") ?? submitDoc.QuerySelector("form");

            if (callbackForm != null)
            {
                var callbackAction = callbackForm.GetAttribute("action");
                var callbackMethodData = callbackForm.QuerySelector("[name='threeDSMethodData']")?.GetAttribute("value");

                // Step 6: POST to the callback URL
                if (!string.IsNullOrEmpty(callbackAction) && !string.IsNullOrEmpty(callbackMethodData))
                {
                    var callbackRequest = new HttpRequestMessage(HttpMethod.Post, callbackAction);
                    callbackRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "threeDSMethodData", callbackMethodData }
                    });
                    callbackRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    callbackRequest.Headers.Add("Origin", $"{methodUri.Scheme}://{methodUri.Host}");
                    callbackRequest.Headers.Add("Referer", $"{methodUri.Scheme}://{methodUri.Host}/");
                    callbackRequest.Headers.Add("Sec-Fetch-Site", "cross-site");
                    callbackRequest.Headers.Add("Sec-Fetch-Mode", "navigate");
                    callbackRequest.Headers.Add("Sec-Fetch-Dest", "iframe");

                    await _http.SendAsync(callbackRequest);
                }
            }

            data.ThreeDSMethodNotificationUrl = callbackUrl;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Handle SecureSuite 3DS Method");
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
                return StepResult<PaymentStepData>.Failure("Could not extract redirect URL", "Submit Device Information");
            }

            var redirectUrl = HttpUtility.HtmlDecode(redirectUrlMatch.Groups[1].Value);

            // Check if this is a direct success (no 3DS challenge required)
            if (redirectUrl.Contains("paymentSuccess", StringComparison.OrdinalIgnoreCase))
            {
                // Payment completed without 3DS challenge
                data.ThreeDSComplete = true;
                data.ThreeDSRequiresOtp = false;
                data.ThreeDSRedirectUrl = null; // No 3DS redirect needed
            }
            else
            {
                // 3DS challenge required
                data.ThreeDSRedirectUrl = redirectUrl;
            }

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit Device Information");
        }
    }

    private async Task<StepResult<PaymentStepData>> Handle3DSRedirectStepAsync(PaymentStepData data)
    {
        // Skip if payment completed without 3DS challenge
        if (data.ThreeDSComplete || string.IsNullOrEmpty(data.ThreeDSRedirectUrl))
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            // ThreeDSRedirectUrl points to payment.anzworldline-solutions.com.au/v1/redirect/handlerequest/{guid}
            // This returns an HTML page with auto-submit form to RSA 3DS Auth

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
                return StepResult<PaymentStepData>.Failure($"HTTP {response.StatusCode}", "Handle 3DS Redirect");

            // Step 2: Parse the auto-submit form that redirects to RSA 3DS Auth
            var doc = await _htmlParser.ParseDocumentAsync(content);
            var form = doc.QuerySelector("form#redirect") ?? doc.QuerySelector("form");

            if (form == null)
                return StepResult<PaymentStepData>.Failure("Could not find redirect form", "Handle 3DS Redirect");

            var formAction = form.GetAttribute("action");
            var creq = form.QuerySelector("[name='creq']")?.GetAttribute("value");
            var threeDSSessionData = form.QuerySelector("[name='threeDSSessionData']")?.GetAttribute("value");

            if (string.IsNullOrWhiteSpace(formAction) || string.IsNullOrWhiteSpace(creq))
                return StepResult<PaymentStepData>.Failure("Could not extract RSA 3DS form data", "Handle 3DS Redirect");

            data.ThreeDSChallengeUrl = HttpUtility.HtmlDecode(formAction);
            data.ThreeDSCReq = creq;
            data.ThreeDSSessionData = threeDSSessionData;

            // Decode creq to extract acsTransID and other data
            // creq is base64 encoded JSON: {"acsTransID":"...","challengeWindowSize":"01","messageType":"CReq","messageVersion":"2.2.0","threeDSServerTransID":"..."}
            try
            {
                var creqJson = Encoding.UTF8.GetString(Convert.FromBase64String(creq));
                var creqData = JsonDocument.Parse(creqJson);
                data.ThreeDSAcsTransId = creqData.RootElement.GetProperty("acsTransID").GetString();
                data.ThreeDSMessageVersion = creqData.RootElement.GetProperty("messageVersion").GetString();
                data.ThreeDSChallengeWindowSize = creqData.RootElement.GetProperty("challengeWindowSize").GetString();
            }
            catch
            {
                // If we can't decode, we'll try to extract from the challenge response
            }

            // Extract issuer from RSA 3DS URL
            // Format: https://www.rsa3dsauth.co.uk/3ds2/cReqWebBased?issuer=national_australia
            var challengeUri = new Uri(data.ThreeDSChallengeUrl);
            var queryParams = HttpUtility.ParseQueryString(challengeUri.Query);
            data.ThreeDSIssuerId = queryParams["issuer"] ?? "national_australia";

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Handle 3DS Redirect");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitRsa3DSChallengeStepAsync(PaymentStepData data)
    {
        // Skip if payment completed without 3DS challenge
        if (data.ThreeDSComplete || string.IsNullOrEmpty(data.ThreeDSChallengeUrl))
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            // POST to RSA 3DS Auth cReqWebBased endpoint
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

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"RSA 3DS challenge failed: {response.StatusCode}", "Submit 3DS Challenge");

            // Store the response for reference
            data.ThreeDSChallengeResponseContent = content;

            // Parse the challenge page to extract form data and submit "Continue" to trigger SMS
            var doc = await _htmlParser.ParseDocumentAsync(content);
            var form = doc.QuerySelector("form#mainForm") ?? doc.QuerySelector("form");

            if (form == null)
                return StepResult<PaymentStepData>.Failure("Could not find challenge form", "Submit 3DS Challenge");

            // Extract hidden fields from the form
            var challengeWindowSize = form.QuerySelector("[name='challengeWindowSize']")?.GetAttribute("value") ?? "01";
            var threeDSServerTransID = form.QuerySelector("[name='threeDSServerTransID']")?.GetAttribute("value");
            var messageVersion = form.QuerySelector("[name='messageVersion']")?.GetAttribute("value") ?? "2.2.0";
            var acsTransID = form.QuerySelector("[name='acsTransID']")?.GetAttribute("value");

            // Update data with extracted values (these are more reliable than decoded creq)
            if (!string.IsNullOrEmpty(acsTransID))
                data.ThreeDSAcsTransId = acsTransID;
            if (!string.IsNullOrEmpty(threeDSServerTransID))
                data.ThreeDSServerTransactionId = threeDSServerTransID;
            if (!string.IsNullOrEmpty(messageVersion))
                data.ThreeDSMessageVersion = messageVersion;
            data.ThreeDSChallengeWindowSize = challengeWindowSize;

            // Get the form action URL (challengeSubmit)
            var formAction = form.GetAttribute("action");
            if (string.IsNullOrEmpty(formAction))
            {
                var challengeUri = new Uri(data.ThreeDSChallengeUrl);
                formAction = $"{challengeUri.Scheme}://{challengeUri.Host}/3ds2/challengeSubmit?issuer={data.ThreeDSIssuerId}";
            }

            // Check if there's a radio button for SMS selection (dataEntry with value like "001")
            var smsRadio = form.QuerySelector("input[name='dataEntry'][type='radio']");
            var smsSelectionValue = smsRadio?.GetAttribute("value") ?? "001";

            // Submit the form to trigger SMS (click "Continue")
            var triggerSmsData = new Dictionary<string, string>
            {
                { "challengeWindowSize", challengeWindowSize },
                { "threeDSServerTransID", threeDSServerTransID ?? data.ThreeDSServerTransactionId },
                { "messageVersion", messageVersion },
                { "acsTransID", acsTransID ?? data.ThreeDSAcsTransId },
                { "dataEntry", smsSelectionValue }
            };

            var triggerRequest = new HttpRequestMessage(HttpMethod.Post, formAction)
            {
                Content = new FormUrlEncodedContent(triggerSmsData)
            };

            var challengeUri2 = new Uri(data.ThreeDSChallengeUrl);
            triggerRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            triggerRequest.Headers.Add("Origin", $"{challengeUri2.Scheme}://{challengeUri2.Host}");
            triggerRequest.Headers.Add("Referer", data.ThreeDSChallengeUrl);
            triggerRequest.Headers.Add("Upgrade-Insecure-Requests", "1");
            triggerRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
            triggerRequest.Headers.Add("Sec-Fetch-Mode", "navigate");
            triggerRequest.Headers.Add("Sec-Fetch-User", "?1");
            triggerRequest.Headers.Add("Sec-Fetch-Dest", "iframe");

            var triggerResponse = await _http.SendAsync(triggerRequest);
            var triggerContent = await triggerResponse.Content.ReadAsStringAsync();

            if (!triggerResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Failed to trigger SMS: {triggerResponse.StatusCode}", "Submit 3DS Challenge");

            // Store the OTP entry page content
            data.ThreeDSChallengeResponseContent = triggerContent;

            // RSA 3DS Auth requires OTP challenge
            data.ThreeDSRequiresOtp = true;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit 3DS Challenge");
        }
    }

    private async Task<StepResult<PaymentStepData>> SubmitRsaOtpStepAsync(PaymentStepData data)
    {
        // Skip if payment already complete or OTP not required
        if (data.ThreeDSComplete || !data.ThreeDSRequiresOtp)
            return StepResult<PaymentStepData>.Success(data);

        try
        {
            // Wait for OTP from SMS provider
            string otp;
            try
            {
                otp = await _smsProvider.GetOtpAsync();
            }
            catch (Exception ex)
            {
                return StepResult<PaymentStepData>.Failure($"Failed to retrieve OTP: {ex.Message}", "Submit OTP");
            }

            // Build the RSA 3DS challengeSubmit URL
            // Format: https://www.rsa3dsauth.co.uk/3ds2/challengeSubmit?issuer=national_australia
            var challengeUri = new Uri(data.ThreeDSChallengeUrl);
            var challengeSubmitUrl = $"{challengeUri.Scheme}://{challengeUri.Host}/3ds2/challengeSubmit?issuer={data.ThreeDSIssuerId}";

            // Submit OTP to RSA 3DS Auth
            var submitData = new Dictionary<string, string>
            {
                { "challengeWindowSize", data.ThreeDSChallengeWindowSize ?? "01" },
                { "threeDSServerTransID", data.ThreeDSServerTransactionId },
                { "messageVersion", data.ThreeDSMessageVersion ?? "2.2.0" },
                { "acsTransID", data.ThreeDSAcsTransId },
                { "dataEntry", otp }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, challengeSubmitUrl)
            {
                Content = new FormUrlEncodedContent(submitData)
            };

            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            request.Headers.Add("Origin", $"{challengeUri.Scheme}://{challengeUri.Host}");
            request.Headers.Add("Referer", data.ThreeDSChallengeUrl);
            request.Headers.Add("Upgrade-Insecure-Requests", "1");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-User", "?1");
            request.Headers.Add("Sec-Fetch-Dest", "iframe");

            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"OTP submission failed: {response.StatusCode}", "Submit OTP");

            // Check for error indicators in the response
            // The response is an HTML page - if OTP is wrong, it shows the challenge page again
            // If OTP is correct, it shows a success/redirect page
            if (content.Contains("re-enter", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("try again", StringComparison.OrdinalIgnoreCase))
            {
                return StepResult<PaymentStepData>.Failure("Invalid OTP code", "Submit OTP");
            }

            // Parse response to check for success indicators or final redirect
            var doc = await _htmlParser.ParseDocumentAsync(content);

            // Look for a form that posts back to the payment provider (success case)
            var resultForm = doc.QuerySelector("form");
            if (resultForm != null)
            {
                var formAction = resultForm.GetAttribute("action");

                // If the form posts back to anzworldline, that's the success callback
                if (formAction?.Contains("anzworldline", StringComparison.OrdinalIgnoreCase) == true ||
                    formAction?.Contains("payment", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Extract any result data from the form
                    var cres = resultForm.QuerySelector("[name='cres']")?.GetAttribute("value");
                    if (!string.IsNullOrEmpty(cres))
                    {
                        data.ThreeDSCallbackData = cres;
                    }

                    // Submit the result form to complete the flow
                    var resultFormData = new Dictionary<string, string>();
                    foreach (var input in resultForm.QuerySelectorAll("input[type='hidden']"))
                    {
                        var name = input.GetAttribute("name");
                        var value = input.GetAttribute("value");
                        if (!string.IsNullOrEmpty(name))
                        {
                            resultFormData[name] = value ?? "";
                        }
                    }

                    if (resultFormData.Count > 0)
                    {
                        var resultRequest = new HttpRequestMessage(HttpMethod.Post, formAction)
                        {
                            Content = new FormUrlEncodedContent(resultFormData)
                        };
                        resultRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        resultRequest.Headers.Add("Origin", $"{challengeUri.Scheme}://{challengeUri.Host}");
                        resultRequest.Headers.Add("Referer", challengeSubmitUrl);

                        await _http.SendAsync(resultRequest);
                    }

                    data.ThreeDSComplete = true;
                    data.ThreeDSRequiresOtp = false;
                }
            }

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