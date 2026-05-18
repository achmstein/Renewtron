using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

namespace Asic.Client.ThreeDS;

/// <summary>
/// Cardinal Commerce 3DS provider (authentication.cardinalcommerce.com).
/// Flow: JSON API — ChooseCredential → VALIDATE page → ValidateCredential with OTP.
/// </summary>
public class CardinalChallengeHandler : IThreeDSChallengeHandler
{
    private const string CardinalBaseUrl = "https://authentication.cardinalcommerce.com";

    private readonly ISmsProvider _smsProvider;

    public CardinalChallengeHandler(ISmsProvider smsProvider)
    {
        _smsProvider = smsProvider;
    }

    public bool CanHandle(Uri challengeUrl) =>
        challengeUrl.Host.Contains("cardinalcommerce", StringComparison.OrdinalIgnoreCase);

    public async Task<StepResult<PaymentStepData>> HandleChallengeAsync(
        HttpClient httpClient,
        HtmlParser htmlParser,
        PaymentStepData data)
    {
        try
        {
            var challengeUri = new Uri(data.ThreeDSChallengeUrl);
            var queryParams = HttpUtility.ParseQueryString(challengeUri.Query);
            var oid = queryParams["oid"];
            var tid = queryParams["tid"];

            if (string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(tid))
                return StepResult<PaymentStepData>.Failure("Cardinal challenge URL missing oid/tid", "Submit OTP");

            data.CardinalOid = oid;
            data.CardinalTid = tid;
            data.ThreeDSAcsTransId = tid;
            data.ThreeDSIssuerId = oid;
            data.CardinalLanguageCode = "en-us";

            // Step 1: ChooseCredential (select SMS OTP option)
            var chooseRequest = new HttpRequestMessage(HttpMethod.Post, $"{CardinalBaseUrl}/Api/2_1_0/NextStep/ChooseCredential")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["CredentialId"] = "a",
                    ["TransactionId"] = tid,
                    ["ChoiceTimeout"] = "",
                    ["IssuerId"] = oid,
                    ["ChoiceType"] = "Grouped",
                    ["X-Requested-With"] = "XMLHttpRequest",
                    ["X-HTTP-Method-Override"] = "FORM"
                })
            };
            chooseRequest.Headers.Add("Accept", "*/*");
            chooseRequest.Headers.Add("Origin", CardinalBaseUrl);
            chooseRequest.Headers.Add("Referer", data.ThreeDSChallengeUrl);
            chooseRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

            var chooseResponse = await httpClient.SendAsync(chooseRequest);
            var chooseContent = await chooseResponse.Content.ReadAsStringAsync();

            if (!chooseResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"ChooseCredential failed: {chooseResponse.StatusCode}", "Submit OTP");

            var chooseJson = JsonNode.Parse(chooseContent)?.AsObject();
            var nextStep = chooseJson?["NextStep"]?.ToString();

            if (nextStep != "VALIDATE")
                return StepResult<PaymentStepData>.Failure($"Unexpected NextStep: {nextStep}", "Submit OTP");

            // Step 2: VALIDATE page (triggers OTP send)
            var validatePageUrl = $"{CardinalBaseUrl}/2_1_0/VALIDATE/{oid}";
            var validatePageRequest = new HttpRequestMessage(HttpMethod.Post, validatePageUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["TransactionId"] = tid,
                    ["GroupId"] = "",
                    ["Type"] = "",
                    ["LanguageCode"] = "en-us",
                    ["CardBrand"] = data.BrandName ?? "Visa",
                    ["Content.TemplateName"] = "Choice_OneUpOneDown_GroupedList_FullWidth",
                    ["Content.WorkflowScreenName"] = "choice",
                    ["ChoiceType"] = "Grouped",
                    ["IssuerId"] = oid
                })
            };
            validatePageRequest.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            validatePageRequest.Headers.Add("Origin", CardinalBaseUrl);
            validatePageRequest.Headers.Add("Referer", data.ThreeDSChallengeUrl);

            var validatePageResponse = await httpClient.SendAsync(validatePageRequest);
            var validatePageContent = await validatePageResponse.Content.ReadAsStringAsync();

            if (!validatePageResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Validate page failed: {validatePageResponse.StatusCode}", "Submit OTP");

            var stepupRequestIdMatch = Regex.Match(validatePageContent, @"StepupRequestId[""']?\s*[:=]\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (stepupRequestIdMatch.Success)
            {
                data.CardinalStepupRequestId = stepupRequestIdMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(data.CardinalStepupRequestId))
            {
                var doc = await htmlParser.ParseDocumentAsync(validatePageContent);
                data.CardinalStepupRequestId = doc.QuerySelector("input[name='StepupRequestId']")?.GetAttribute("value");
            }

            // Step 3: Wait for OTP
            string otp;
            try
            {
                otp = await _smsProvider.GetOtpAsync();
            }
            catch (Exception ex)
            {
                return StepResult<PaymentStepData>.Failure($"Failed to retrieve OTP: {ex.Message}", "Submit OTP");
            }

            // Step 4: ValidateCredential with OTP
            var validateRequest = new HttpRequestMessage(HttpMethod.Post, $"{CardinalBaseUrl}/Api/2_1_0/NextStep/ValidateCredential")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["TransactionId"] = tid,
                    ["StepupRequestId"] = data.CardinalStepupRequestId ?? "",
                    ["ValidateTimeout"] = "",
                    ["IssuerId"] = oid,
                    ["ValidateReloadEnabled"] = "True",
                    ["VisaBehavioralAnalyticsEnabled"] = "False",
                    ["LanguageCode"] = "en-us",
                    ["LanguageShortCode"] = "en",
                    ["CredentialValidationMessage"] = "Please re-enter your code",
                    ["Credential.Value"] = otp,
                    ["Credential.Id"] = "a",
                    ["X-Requested-With"] = "XMLHttpRequest",
                    ["X-HTTP-Method-Override"] = "FORM"
                })
            };
            validateRequest.Headers.Add("Accept", "*/*");
            validateRequest.Headers.Add("Origin", CardinalBaseUrl);
            validateRequest.Headers.Add("Referer", validatePageUrl);
            validateRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

            var validateResponse = await httpClient.SendAsync(validateRequest);
            var validateContent = await validateResponse.Content.ReadAsStringAsync();

            if (!validateResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"ValidateCredential failed: {validateResponse.StatusCode}", "Submit OTP");

            var validateJson = JsonNode.Parse(validateContent)?.AsObject();
            var message = validateJson?["Message"]?.AsObject();
            var messageContent = message?["Content"]?.ToString();

            if (messageContent?.Contains("re-enter your code", StringComparison.OrdinalIgnoreCase) == true ||
                messageContent?.Contains("incorrect", StringComparison.OrdinalIgnoreCase) == true)
            {
                return StepResult<PaymentStepData>.Failure("Invalid OTP code", "Submit OTP");
            }

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
}
