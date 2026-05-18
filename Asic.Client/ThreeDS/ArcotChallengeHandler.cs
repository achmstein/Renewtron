using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using System.Web;

namespace Asic.Client.ThreeDS;

/// <summary>
/// Arcot / Secure7 3DS provider (secure7.arcot.com) — used by Visa cards routed via ANZ Worldline.
/// Flow: POST selected_options to /v1/challenge (triggers SMS) → POST text_input={otp} →
/// POST cres_form to ANZ /v1/redirect/handleresponse.
/// </summary>
public class ArcotChallengeHandler : IThreeDSChallengeHandler
{
    private readonly ISmsProvider _smsProvider;

    public ArcotChallengeHandler(ISmsProvider smsProvider)
    {
        _smsProvider = smsProvider;
    }

    public bool CanHandle(Uri challengeUrl) =>
        challengeUrl.Host.Contains("arcot", StringComparison.OrdinalIgnoreCase);

    public async Task<StepResult<PaymentStepData>> HandleChallengeAsync(
        HttpClient httpClient,
        HtmlParser htmlParser,
        PaymentStepData data)
    {
        try
        {
            // Step 1: parse the credential-selection page returned by /v1/creq
            var selectDoc = await htmlParser.ParseDocumentAsync(data.ThreeDSChallengeResponseContent ?? "");
            var selectForm = selectDoc.QuerySelector("form");

            if (selectForm == null)
                return StepResult<PaymentStepData>.Failure("Could not find Arcot challenge form", "Submit OTP");

            var acsAccountId = selectForm.QuerySelector("input[name='acs_account_id']")?.GetAttribute("value");
            if (string.IsNullOrEmpty(acsAccountId))
                return StepResult<PaymentStepData>.Failure("Arcot form missing acs_account_id", "Submit OTP");

            // Prefer the option whose value contains "mobile" — falls back to the first selected_options
            // input (radio / option element) if no obvious mobile candidate exists.
            var selectedOption = selectForm
                .QuerySelectorAll("input[name='selected_options'], option")
                .Select(el => el.GetAttribute("value"))
                .Where(v => !string.IsNullOrEmpty(v))
                .FirstOrDefault(v => v!.Contains("mobile", StringComparison.OrdinalIgnoreCase))
                ?? "mobilenumber1";

            var challengeUri = ResolveActionUri(selectForm.GetAttribute("action"), data.ThreeDSChallengeUrl);

            data.ThreeDSIssuerId = acsAccountId;

            var triggerSmsRequest = new HttpRequestMessage(HttpMethod.Post, challengeUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["selected_options"] = selectedOption!,
                    ["acs_account_id"] = acsAccountId
                })
            };
            AddNavigationHeaders(triggerSmsRequest, challengeUri, data.ThreeDSChallengeUrl);

            var triggerSmsResponse = await httpClient.SendAsync(triggerSmsRequest);
            var verifyPageContent = await triggerSmsResponse.Content.ReadAsStringAsync();

            if (!triggerSmsResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Arcot SMS trigger failed: {triggerSmsResponse.StatusCode}", "Submit OTP");

            // Step 2: parse the verify (OTP entry) form
            var verifyDoc = await htmlParser.ParseDocumentAsync(verifyPageContent);
            var verifyForm = verifyDoc.QuerySelector("form#verify_form") ?? verifyDoc.QuerySelector("form");

            if (verifyForm == null || verifyForm.QuerySelector("input[name='text_input']") == null)
                return StepResult<PaymentStepData>.Failure("Arcot OTP-entry form not returned", "Submit OTP");

            var verifyAcsAccountId = verifyForm.QuerySelector("input[name='acs_account_id']")?.GetAttribute("value") ?? acsAccountId;
            var verifyChallengeUri = ResolveActionUri(verifyForm.GetAttribute("action"), data.ThreeDSChallengeUrl);

            // Step 3: wait for OTP
            string otp;
            try
            {
                otp = await _smsProvider.GetOtpAsync();
            }
            catch (Exception ex)
            {
                return StepResult<PaymentStepData>.Failure($"Failed to retrieve OTP: {ex.Message}", "Submit OTP");
            }

            // Step 4: submit OTP
            var otpFields = new Dictionary<string, string>
            {
                ["text_input"] = otp,
                ["acs_account_id"] = verifyAcsAccountId,
                ["bb_user_transaction_id"] = verifyForm.QuerySelector("input[name='bb_user_transaction_id']")?.GetAttribute("value") ?? "",
                ["bb_profile_id"] = verifyForm.QuerySelector("input[name='bb_profile_id']")?.GetAttribute("value") ?? "",
                ["bb_data"] = verifyForm.QuerySelector("input[name='bb_data']")?.GetAttribute("value") ?? ""
            };

            var otpRequest = new HttpRequestMessage(HttpMethod.Post, verifyChallengeUri)
            {
                Content = new FormUrlEncodedContent(otpFields)
            };
            AddNavigationHeaders(otpRequest, verifyChallengeUri, data.ThreeDSChallengeUrl);

            var otpResponse = await httpClient.SendAsync(otpRequest);
            var otpResponseContent = await otpResponse.Content.ReadAsStringAsync();

            if (!otpResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"Arcot OTP submission failed: {otpResponse.StatusCode}", "Submit OTP");

            // If the server returned the same verify form (no cres_form), the OTP was rejected
            if (otpResponseContent.Contains("verify_form", StringComparison.OrdinalIgnoreCase) &&
                !otpResponseContent.Contains("cres_form", StringComparison.OrdinalIgnoreCase) &&
                !otpResponseContent.Contains("name=\"cres\"", StringComparison.OrdinalIgnoreCase))
            {
                return StepResult<PaymentStepData>.Failure("Invalid OTP code", "Submit OTP");
            }

            // Step 5: post cres_form back to ANZ handleresponse — same shape as RSA's cres_submit
            var (cresOk, cresError) = await RsaChallengeHandler.PostCresFormAsync(
                httpClient, htmlParser, otpResponseContent, verifyChallengeUri, "cres_form");

            if (!cresOk)
                return StepResult<PaymentStepData>.Failure(cresError!, "Submit OTP");

            data.ThreeDSComplete = true;
            data.ThreeDSRequiresOtp = false;

            return StepResult<PaymentStepData>.Success(data);
        }
        catch (Exception ex)
        {
            return StepResult<PaymentStepData>.Failure(ex.Message, "Submit OTP");
        }
    }

    private static Uri ResolveActionUri(string? action, string fallbackReferer)
    {
        var decoded = HttpUtility.HtmlDecode(action ?? "");
        if (Uri.IsWellFormedUriString(decoded, UriKind.Absolute))
            return new Uri(decoded);

        return new Uri(new Uri(fallbackReferer), decoded);
    }

    private static void AddNavigationHeaders(HttpRequestMessage request, Uri target, string referer)
    {
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Origin", $"{target.Scheme}://{target.Host}");
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "iframe");
    }
}
