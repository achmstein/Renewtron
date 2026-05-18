using AngleSharp.Html.Parser;
using Asic.Client.Abstractions;
using Asic.Client.Models;
using System.Net;
using System.Web;

namespace Asic.Client.ThreeDS;

/// <summary>
/// RSA 3DS provider (rsa3dsauth.co.uk) — used by NAB / ANZ Worldline issuers.
/// Flow: optional credential-select POST (triggers SMS) → OTP POST → cres POST back to ANZ.
/// </summary>
public class RsaChallengeHandler : IThreeDSChallengeHandler
{
    private readonly ISmsProvider _smsProvider;

    public RsaChallengeHandler(ISmsProvider smsProvider)
    {
        _smsProvider = smsProvider;
    }

    public bool CanHandle(Uri challengeUrl) =>
        challengeUrl.Host.Contains("rsa3dsauth", StringComparison.OrdinalIgnoreCase);

    public async Task<StepResult<PaymentStepData>> HandleChallengeAsync(
        HttpClient httpClient,
        HtmlParser htmlParser,
        PaymentStepData data)
    {
        try
        {
            // RSA returns a credential-selection page first (radio input named "dataEntry"
            // valued "001"). Submitting that selection is what triggers the issuer to dispatch
            // the OTP SMS; only the response to that submit is the real OTP-entry page
            // (password input named "dataEntry"). Without this step, polling for the SMS
            // waits indefinitely because the bank never sent one.
            var challengeDoc = await htmlParser.ParseDocumentAsync(data.ThreeDSChallengeResponseContent ?? "");
            var mainForm = challengeDoc.QuerySelector("form#mainForm") ?? challengeDoc.QuerySelector("form");

            if (mainForm == null)
                return StepResult<PaymentStepData>.Failure("Could not find RSA challenge form", "Submit OTP");

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

            var submitUri = Uri.IsWellFormedUriString(submitAction, UriKind.Absolute)
                ? new Uri(submitAction)
                : new Uri(new Uri(data.ThreeDSChallengeUrl), submitAction);

            var radioOption = mainForm.QuerySelector("input[name='dataEntry'][type='radio']");
            if (radioOption != null)
            {
                var radioValue = radioOption.GetAttribute("value") ?? "001";

                var selectRequest = new HttpRequestMessage(HttpMethod.Post, submitUri)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["challengeWindowSize"] = challengeWindowSize,
                        ["threeDSServerTransID"] = threeDSServerTransID,
                        ["messageVersion"] = messageVersion,
                        ["acsTransID"] = acsTransID,
                        ["dataEntry"] = radioValue
                    })
                };
                AddNavigationHeaders(selectRequest, submitUri, data.ThreeDSChallengeUrl);

                var selectResponse = await httpClient.SendAsync(selectRequest);
                var selectContent = await selectResponse.Content.ReadAsStringAsync();

                if (!selectResponse.IsSuccessStatusCode)
                    return StepResult<PaymentStepData>.Failure($"RSA credential selection failed: {selectResponse.StatusCode}", "Submit OTP");

                var otpDoc = await htmlParser.ParseDocumentAsync(selectContent);
                var otpForm = otpDoc.QuerySelector("form#mainForm") ?? otpDoc.QuerySelector("form");

                if (otpForm == null || otpForm.QuerySelector("input[name='dataEntry']") == null)
                    return StepResult<PaymentStepData>.Failure("RSA OTP-entry form not returned after credential selection", "Submit OTP");

                challengeWindowSize = otpForm.QuerySelector("input[name='challengeWindowSize']")?.GetAttribute("value") ?? challengeWindowSize;
                threeDSServerTransID = otpForm.QuerySelector("input[name='threeDSServerTransID']")?.GetAttribute("value") ?? threeDSServerTransID;
                messageVersion = otpForm.QuerySelector("input[name='messageVersion']")?.GetAttribute("value") ?? messageVersion;
                acsTransID = otpForm.QuerySelector("input[name='acsTransID']")?.GetAttribute("value") ?? acsTransID;

                var newAction = HttpUtility.HtmlDecode(otpForm.GetAttribute("action") ?? submitAction);
                submitUri = Uri.IsWellFormedUriString(newAction, UriKind.Absolute)
                    ? new Uri(newAction)
                    : new Uri(new Uri(data.ThreeDSChallengeUrl), newAction);

                data.ThreeDSAcsTransId = acsTransID;
                data.ThreeDSTransactionId = threeDSServerTransID;
                data.ThreeDSMessageVersion = messageVersion;
                data.ThreeDSChallengeWindowSize = challengeWindowSize;
            }

            string otp;
            try
            {
                otp = await _smsProvider.GetOtpAsync();
            }
            catch (Exception ex)
            {
                return StepResult<PaymentStepData>.Failure($"Failed to retrieve OTP: {ex.Message}", "Submit OTP");
            }

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
            AddNavigationHeaders(submitRequest, submitUri, data.ThreeDSChallengeUrl);

            var submitResponse = await httpClient.SendAsync(submitRequest);
            var submitContent = await submitResponse.Content.ReadAsStringAsync();

            if (!submitResponse.IsSuccessStatusCode)
                return StepResult<PaymentStepData>.Failure($"RSA challengeSubmit failed: {submitResponse.StatusCode}", "Submit OTP");

            // If the server returned the same OTP form again, the code was rejected.
            if (submitContent.Contains("challengeSubmit", StringComparison.OrdinalIgnoreCase) &&
                submitContent.Contains("dataEntry", StringComparison.OrdinalIgnoreCase) &&
                !submitContent.Contains("cres_submit", StringComparison.OrdinalIgnoreCase) &&
                !submitContent.Contains("name=\"cres\"", StringComparison.OrdinalIgnoreCase))
            {
                return StepResult<PaymentStepData>.Failure("Invalid OTP code", "Submit OTP");
            }

            var (cresOk, cresError) = await PostCresFormAsync(httpClient, htmlParser, submitContent, submitUri, "cres_submit");
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

    internal static async Task<(bool Success, string? Error)> PostCresFormAsync(
        HttpClient httpClient,
        HtmlParser htmlParser,
        string content,
        Uri referer,
        string formId)
    {
        var doc = await htmlParser.ParseDocumentAsync(content);
        var cresForm = doc.QuerySelector($"form#{formId}") ?? doc.QuerySelector("form");

        if (cresForm == null)
            return (false, "Could not find cres form after OTP");

        var cresAction = HttpUtility.HtmlDecode(cresForm.GetAttribute("action") ?? "");
        var cresValue = cresForm.QuerySelector("input[name='cres']")?.GetAttribute("value");
        var cresSessionData = cresForm.QuerySelector("input[name='threeDSSessionData']")?.GetAttribute("value");

        if (string.IsNullOrWhiteSpace(cresAction) || string.IsNullOrWhiteSpace(cresValue))
            return (false, "cres form missing required fields");

        var cresUri = Uri.IsWellFormedUriString(cresAction, UriKind.Absolute)
            ? new Uri(cresAction)
            : new Uri(referer, cresAction);

        var finalForm = new Dictionary<string, string> { ["cres"] = cresValue };
        if (!string.IsNullOrWhiteSpace(cresSessionData))
            finalForm["threeDSSessionData"] = cresSessionData;

        var finalRequest = new HttpRequestMessage(HttpMethod.Post, cresUri)
        {
            Content = new FormUrlEncodedContent(finalForm)
        };
        finalRequest.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        finalRequest.Headers.TryAddWithoutValidation("Origin", $"{referer.Scheme}://{referer.Host}");
        finalRequest.Headers.TryAddWithoutValidation("Referer", referer.ToString());
        finalRequest.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

        var finalResponse = await httpClient.SendAsync(finalRequest);

        if (!finalResponse.IsSuccessStatusCode
            && finalResponse.StatusCode != HttpStatusCode.Found
            && finalResponse.StatusCode != HttpStatusCode.Redirect)
        {
            return (false, $"cres submission failed: {finalResponse.StatusCode}");
        }

        return (true, null);
    }
}
