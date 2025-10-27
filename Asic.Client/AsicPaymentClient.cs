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
                .ThenAsync("Complete Payment Gateway", CompletePaymentGatewayStepAsync)
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
        data.AdfWindowId = adfWindowId;
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

            var content = await response.Content.ReadAsStringAsync();

            var json = JsonNode.Parse(content).AsObject();

            data.ThreeDSServerTransactionId = json["threeDSServerTransactionId"];

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

            // Polly retry policy (fixed 2-second interval)
            AsyncRetryPolicy<HttpResponseMessage> retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>() // request timeout
                .OrResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.RequestTimeout)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: _ => TimeSpan.FromSeconds(2),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        Console.WriteLine($"[Retry {retryAttempt}] Retrying after 2 seconds due to {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                    });

            // Execute the request with retry
            var response = await retryPolicy.ExecuteAsync(() => _http.SendAsync(request));

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

    private async Task<StepResult<PaymentStepData>> CompletePaymentGatewayStepAsync(PaymentStepData data)
    {
        try
        {
            // Step 1: Submit device information and get 3DS redirect URL
            var (success, redirectUrl) = await SubmitDeviceInformationAsync(data);
            if (!success || string.IsNullOrEmpty(redirectUrl))
            {
                return StepResult<PaymentStepData>.Failure("Failed to submit device information", "Complete Payment Gateway");
            }

            // Step 2: Process 3DS authentication flow
            var threeDsResult = await Process3DSAuthenticationAsync(redirectUrl, data);
            if (!threeDsResult.Success)
            {
                return StepResult<PaymentStepData>.Failure($"3DS authentication failed: {threeDsResult.Message}", "Complete Payment Gateway");
            }

            // Step 3: Close payment window
            await ClosePaymentWindowAsync(data.AdfWindowId);

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure($"Payment gateway completion failed: {ex.Message}", "Complete Payment Gateway");
        }
    }

    private async Task<(bool Success, string RedirectUrl)> SubmitDeviceInformationAsync(PaymentStepData data)
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
                return (false, null);
            }

            var redirectUrlMatch = Regex.Match(content, @"window\.location\.href\s*=\s*'([^']+)'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!redirectUrlMatch.Success)
            {
                return (false, null);
            }

            return (true, HttpUtility.HtmlDecode(redirectUrlMatch.Groups[1].Value));
        }
        catch
        {
            return (false, null);
        }
    }

    private async Task<PaymentResult> Process3DSAuthenticationAsync(string redirectUrl, PaymentStepData data)
    {
        try
        {
            // Step 1: GET the initial 3DS redirect page
            var request1 = new HttpRequestMessage(HttpMethod.Get, redirectUrl);
            request1.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request1.Headers.Add("Sec-Fetch-Site", "cross-site");
            request1.Headers.Add("Sec-Fetch-Mode", "navigate");
            request1.Headers.Add("Sec-Fetch-Dest", "iframe");
            request1.Headers.Add("Referer", "https://regpayment.asic.gov.au/");

            var response1 = await _http.SendAsync(request1);
            var content1 = await response1.Content.ReadAsStringAsync();

            // Extract iframe src using AngleSharp
            var doc1 = await _htmlParser.ParseDocumentAsync(content1);
            var iframeSrc = doc1.QuerySelector("iframe")?.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(iframeSrc))
                return PaymentResult.Failed("Could not find 3DS iframe URL");

            iframeSrc = HttpUtility.HtmlDecode(iframeSrc);

            // Step 2: GET the iframe content (contains the form)
            var request2 = new HttpRequestMessage(HttpMethod.Get, iframeSrc);
            request2.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request2.Headers.Add("Sec-Fetch-Site", "same-origin");
            request2.Headers.Add("Sec-Fetch-Mode", "navigate");
            request2.Headers.Add("Sec-Fetch-Dest", "iframe");
            request2.Headers.Add("Referer", redirectUrl);

            var response2 = await _http.SendAsync(request2);
            var content2 = await response2.Content.ReadAsStringAsync();

            // Extract form action and threeDSMethodData using AngleSharp
            var doc2 = await _htmlParser.ParseDocumentAsync(content2);
            var form = doc2.QuerySelector("form");
            var formAction = form?.GetAttribute("action");
            var threeDSMethodData = form?.QuerySelector("[name='threeDSMethodData']")?.GetAttribute("value");

            if (string.IsNullOrWhiteSpace(formAction) || string.IsNullOrWhiteSpace(threeDSMethodData))
                return PaymentResult.Failed("Could not extract 3DS form data");

            formAction = HttpUtility.HtmlDecode(formAction);

            // Step 3: POST to form action (CardinalCommerce)
            var request3 = new HttpRequestMessage(HttpMethod.Post, formAction)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "threeDSMethodData", threeDSMethodData }
                })
            };

            request3.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request3.Headers.Add("Origin", new Uri(iframeSrc).GetLeftPart(UriPartial.Authority));
            request3.Headers.Add("Sec-Fetch-Site", "cross-site");
            request3.Headers.Add("Sec-Fetch-Mode", "navigate");
            request3.Headers.Add("Sec-Fetch-Dest", "iframe");
            request3.Headers.Add("Referer", iframeSrc);

            var response3 = await _http.SendAsync(request3);
            var content3 = await response3.Content.ReadAsStringAsync();

            // Step 4: Extract callback form (if present)
            var doc3 = await _htmlParser.ParseDocumentAsync(content3);
            var callbackUrl = doc3?.QuerySelector("#notificationUrl").GetAttribute("value");
            var callbackData = doc3?.QuerySelector("#base64payload").GetAttribute("value");

            if (string.IsNullOrWhiteSpace(callbackUrl))
            {
                return PaymentResult.Succeeded(null);
            }

            // Step 5: POST callback
            var request4 = new HttpRequestMessage(HttpMethod.Post, callbackUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "threeDSMethodData", callbackData }
                })
            };

            request4.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request4.Headers.Add("Origin", "https://geoissuer.cardinalcommerce.com");
            request4.Headers.Add("Sec-Fetch-Site", "cross-site");
            request4.Headers.Add("Sec-Fetch-Mode", "navigate");
            request4.Headers.Add("Sec-Fetch-Dest", "iframe");
            request4.Headers.Add("Referer", "https://geoissuer.cardinalcommerce.com/");

            var response4 = await _http.SendAsync(request4);
            var content4 = await response4.Content.ReadAsStringAsync();

            // Step 6: Extract result redirect URL
            var doc4 = await _htmlParser.ParseDocumentAsync(content4);
            var script = doc4.Scripts.FirstOrDefault(s => s.TextContent.Contains("window.open"));
            var resultUrl = Regex.Match(script?.TextContent ?? "", @"window\.open\(['""]([^'""]+)['""]").Groups[1].Value;

            if (string.IsNullOrWhiteSpace(resultUrl))
                return PaymentResult.Failed("Could not find result redirect URL");

            // Step 7: GET result page (check if challenge)
            var request5 = new HttpRequestMessage(HttpMethod.Get, resultUrl);
            request5.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request5.Headers.Add("Sec-Fetch-Site", "same-origin");
            request5.Headers.Add("Sec-Fetch-Mode", "navigate");
            request5.Headers.Add("Sec-Fetch-Dest", "iframe");
            request5.Headers.Add("Referer", callbackUrl);

            var response5 = await _http.SendAsync(request5);
            var content5 = await response5.Content.ReadAsStringAsync();

            var doc5 = await _htmlParser.ParseDocumentAsync(content5);
            var challengeForm = doc5.QuerySelector("form");
            if (challengeForm != null)
            {
                var challengeUrl = HttpUtility.HtmlDecode(challengeForm.GetAttribute("action"));
                var threeDSSessionData = challengeForm.QuerySelector("[name='threeDSSessionData']")?.GetAttribute("value");
                var creq = challengeForm.QuerySelector("[name='creq']")?.GetAttribute("value");

                if (string.IsNullOrWhiteSpace(challengeUrl) || string.IsNullOrWhiteSpace(threeDSSessionData) || string.IsNullOrWhiteSpace(creq))
                    return PaymentResult.Failed("Could not extract challenge form data");

                var request6 = new HttpRequestMessage(HttpMethod.Post, challengeUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "threeDSMethodData", threeDSSessionData },
                        { "creq", creq }
                    })
                };

                request6.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                request6.Headers.Add("Origin", "https://methodurl.psp-solutions.com");
                request6.Headers.Add("Referer", "https://methodurl.psp-solutions.com/");
                request6.Headers.Add("DNT", "1");
                request6.Headers.Add("Upgrade-Insecure-Requests", "1");
                request6.Headers.Add("Sec-Fetch-Site", "cross-site");
                request6.Headers.Add("Sec-Fetch-Mode", "navigate");
                request6.Headers.Add("Sec-Fetch-Dest", "iframe");
                request6.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36");

                var response6 = await _http.SendAsync(request6);
                var content6 = await response6.Content.ReadAsStringAsync();

                // Optional: parse or log the CRes result
                if (!response6.IsSuccessStatusCode)
                    return PaymentResult.Failed($"CReq submission failed: {response6.StatusCode}");

                if (content6.Contains("Your One-time Passcode has been sent"))
                {
                    var doc6 = await _htmlParser.ParseDocumentAsync(content6);

                    var issuerId = doc6.QuerySelector("#IssuerId")?.GetAttribute("value");
                    var transactionId = doc6.QuerySelector("#TransactionId")?.GetAttribute("value");

                    // Step 2: Get OTP from your service
                    var otp = "123456"; // implement your own method

                    // Step 3: Build form data for ValidateCredential
                    var postData = new Dictionary<string, string>()
                    {
                        ["LanguageCode"] = "en-us",
                        ["LanguageShortCode"] = "en",
                        ["CredentialValidationMessage"] = "Please re-enter your code",
                        ["Credential.Value"] = otp,
                        ["Credential.Id"] = "a",
                        ["TransactionId"] = transactionId,
                        ["ValidateTimeout"] = "",
                        ["IssuerId"] = issuerId,
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

                    validateRequest.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                    validateRequest.Headers.Referrer = new Uri(challengeUrl);

                    // Step 4: Submit OTP
                    var validateResponse = await _http.SendAsync(validateRequest);
                    var validateJson = await validateResponse.Content.ReadAsStringAsync();

                    if (!validateResponse.IsSuccessStatusCode)
                        throw new Exception($"OTP validation failed: {validateJson}");

                    using var jsonDoc = JsonDocument.Parse(validateJson);
                    var nextStep = jsonDoc.RootElement.GetProperty("NextStep").GetString();

                    if (!string.Equals(nextStep, "TERM", StringComparison.OrdinalIgnoreCase))
                        throw new Exception($"Unexpected next step: {nextStep}");

                    var termData = new Dictionary<string, string>
                    {
                        ["TransactionId"] = transactionId,
                        ["IssuerId"] = issuerId
                    };

                    var termUrl = "https://authentication.cardinalcommerce.com/api/2_1_0/nextstep/term";
                    var termRequest = new HttpRequestMessage(HttpMethod.Post, termUrl)
                    {
                        Content = new FormUrlEncodedContent(termData)
                    };
                    termRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
                    termRequest.Headers.Add("Origin", "https://authentication.cardinalcommerce.com");
                    termRequest.Headers.Add("Origin", "https://authentication.cardinalcommerce.com");
                    termRequest.Headers.Referrer = new Uri(challengeUrl);

                    var termResponse = await _http.SendAsync(termRequest);
                    var termJson = await termResponse.Content.ReadAsStringAsync();

                    if (!termResponse.IsSuccessStatusCode)
                        throw new Exception($"TERM step failed: {termJson}");

                    using var termDoc = JsonDocument.Parse(termJson);
                    var payload = termDoc.RootElement.GetProperty("Payload");

                    var cres = payload.GetProperty("CRes").GetString();
                    var notificationUrl = payload.GetProperty("NotificationUrl").GetString();

                    // Step 6: Send CRes back to ACS Notification URL
                    var finalResponse = await _http.PostAsync(notificationUrl,
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["cres"] = cres,
                            ["threeDSSessionData"] = threeDSSessionData,
                        }));

                    if (!finalResponse.IsSuccessStatusCode)
                        throw new Exception($"Failed to send CRes: {finalResponse.StatusCode}");

                    return PaymentResult.Succeeded(data.HostedTokenizationId);
                }

                return PaymentResult.Failed($"3DS Challenge required. User must enter OTP. Challenge URL: {challengeUrl}");
            }

            return PaymentResult.Succeeded(null);
        }
        catch (Exception ex)
        {
            return PaymentResult.Failed($"3DS authentication failed: {ex.Message}");
        }
    }

    private async Task ClosePaymentWindowAsync(string adfWindowId)
    {
        try
        {
            var closeWindowRequest = new HttpRequestMessage(HttpMethod.Post, "https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx");
            var closeWindowContent = new StringBuilder();
            closeWindowContent.Append($"Adf-Window-Id={adfWindowId}&");
            closeWindowContent.Append("Adf-Page-Id=0");

            closeWindowRequest.Content = new StringContent(closeWindowContent.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
            closeWindowRequest.Headers.Add("Adf-Rich-Message", "true");
            closeWindowRequest.Headers.Add("Adf-Window-Unloaded", "true");

            await _http.SendAsync(closeWindowRequest);
        }
        catch
        {
            // Ignore errors in window close
        }
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

    private string ExtractTokenFromUrl(string url)
    {
        // Extract the last part of the URL path (the token)
        var uri = new Uri(url);
        var segments = uri.Segments;
        return segments[segments.Length - 1];
    }
}