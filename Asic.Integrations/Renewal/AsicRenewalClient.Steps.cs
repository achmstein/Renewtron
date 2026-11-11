using Asic.Integrations.Payment;
using Asic.Integrations.Shared;

namespace Asic.Integrations.Renewal;

/// <summary>
/// The renewal's steps, in the order ASIC's pages come. Each posts exactly what the browser
/// posts on that page (field order included) and checks ASIC's answer before the next runs.
/// Step names and messages are matched by Renewtron's error classifiers: change them together.
/// </summary>
public sealed partial class AsicRenewalClient
{
    private static StepResult<RenewalStepData> Ok(RenewalStepData data) => StepResult<RenewalStepData>.Success(data);
    private static StepResult<RenewalStepData> Fail(string message, string step) => StepResult<RenewalStepData>.Failure(message, step);

    private async Task<StepResult<RenewalStepData>> StartSessionStepAsync(RenewalStepData data)
    {
        // Nothing has been sent to ASIC yet, so a session that won't start is retried.
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                ResetSession();
                var (sessionId, windowId, viewState) = await StartSessionAsync();
                if (!string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(windowId) && !string.IsNullOrEmpty(viewState))
                {
                    data.SessionId = sessionId;
                    data.AdfWindowId = windowId;
                    data.ViewState = viewState;
                    return Ok(data);
                }
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // retried below
            }
            if (attempt < maxAttempts) await _delay(TimeSpan.FromSeconds(attempt * 3));
        }
        return Fail(SessionFailed, "Initialize Session");
    }

    private async Task<StepResult<RenewalStepData>> SubmitAbnStepAsync(RenewalStepData data)
    {
        var (response, content) = await PostAsync(data.AdfWindowId, AbnForm(data.Abn, data.AdfWindowId, data.ViewState));

        if (RenewalPages.Refusal(content) is { } refusal) return Fail(refusal.Message, refusal.Step);
        if (!response.IsSuccessStatusCode || !RenewalPages.AcceptsAbn(content)) return Fail("Failed to submit ABN", "Submit ABN");

        data.PageContent = content;
        return Ok(data);
    }

    private async Task<StepResult<RenewalStepData>> SelectBusinessStepAsync(RenewalStepData data)
    {
        if (RenewalPages.RadioFor(data.PageContent ?? "", data.AccountNumber) is not { Length: > 0 } radio)
            return Fail($"Could not find business name with account number {data.AccountNumber}", "Select Business Name");

        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"bngrp={Encode(radio)}",
            .. Session(data),
            WaitPopupShown,
            .. ValueChange(radio),
        ]));
        if (!response.IsSuccessStatusCode || !RenewalPages.HasNextButton(content))
            return Fail("Failed to select business name", "Select Business Name");

        data.SelectedBusinessNameId = Encode(radio);
        data.PageContent = content;
        return Ok(data);
    }

    private async Task<StepResult<RenewalStepData>> ProceedToRenewalPeriodStepAsync(RenewalStepData data)
    {
        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"bngrp={data.SelectedBusinessNameId}",
            .. Session(data),
            .. Click("tmpt:region:1:form:nextBut"),
        ]));
        if (!response.IsSuccessStatusCode) return Fail("HTTP request failed", "Proceed to Renewal Period");

        // ASIC may resume an earlier session and jump straight to the email page.
        var resumed = RenewalPages.IsResumedAtEmail(content);
        if (!resumed && !RenewalPages.IsRenewalPeriod(content)) return Fail("Unexpected page state", "Proceed to Renewal Period");

        data.TransactionReference = RenewalPages.TransactionReference(content);
        data.JumpedToEmail = resumed;
        data.PageContent = content;
        return Ok(data);
    }

    private async Task<StepResult<RenewalStepData>> SelectRenewalPeriodStepAsync(RenewalStepData data)
    {
        if (data.JumpedToEmail) return Ok(data);

        var period = data.RenewalYears == 3 ? "1" : "0";
        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"tmpt:region:2:form:selRen={period}",
            .. Session(data),
            .. ValueChange("tmpt:region:2:form:selRen"),
        ]));
        if (!response.IsSuccessStatusCode || !RenewalPages.HasNextButton(content))
            return Fail("Failed to select renewal period", "Select Renewal Period");

        data.PeriodValue = period;
        return Ok(data);
    }

    private async Task<StepResult<RenewalStepData>> ProceedToReviewStepAsync(RenewalStepData data)
    {
        if (data.JumpedToEmail) return Ok(data);

        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"tmpt:region:2:form:selRen={data.PeriodValue}",
            .. Session(data),
            .. Click("tmpt:region:2:form:nextButt"),
        ]));
        return response.IsSuccessStatusCode && RenewalPages.IsReview(content)
            ? Ok(data)
            : Fail("Failed to proceed to review", "Proceed to Review");
    }

    /// <summary>Declares as the holder's representative (auth2).</summary>
    private async Task<StepResult<RenewalStepData>> SelectAuthorityDeclarationStepAsync(RenewalStepData data)
    {
        if (data.JumpedToEmail) return Ok(data);

        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            "auth=tmpt%3Aregion%3A3%3Aauth2",
            .. Session(data),
            .. ValueChange("tmpt:region:3:auth2"),
        ]));
        return response.IsSuccessStatusCode && RenewalPages.HasNextButton(content)
            ? Ok(data)
            : Fail("Failed to select authority declaration", "Select Authority Declaration");
    }

    private async Task<StepResult<RenewalStepData>> ProceedToPaymentStepAsync(RenewalStepData data)
    {
        if (data.JumpedToEmail) return Ok(data);

        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            "auth=tmpt%3Aregion%3A3%3Aauth2",
            .. Session(data),
            .. Click("tmpt:region:3:nextButt"),
        ]));
        if (!response.IsSuccessStatusCode || !RenewalPages.IsEmailPage(content)) return Fail("Failed to proceed to payment", "Proceed to Payment");

        data.PageContent = content;
        return Ok(data);
    }

    private async Task<StepResult<RenewalStepData>> SubmitEmailStepAsync(RenewalStepData data)
    {
        var region = RenewalPages.Region(data.PageContent);
        var email = Uri.EscapeDataString(data.Email);
        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"tmpt:region:{region}:form:emIn1={email}",
            $"tmpt:region:{region}:form:emIn2={email}",
            .. Session(data),
            RenderRegion,
            WaitPopupShown,
            .. Click($"tmpt:region:{region}:form:nextButt"),
        ]));
        if (!response.IsSuccessStatusCode || !RenewalPages.IsPaymentPreference(content))
            return Fail(data.JumpedToEmail ? "Failed to submit email (resumed session)" : "Failed to submit email", "Submit Email");

        data.PageContent = content;
        return Ok(data);
    }

    /* ───────────── Pay by card ───────────── */

    /// <summary>Ticks "Pay now" (with "Pay later" firing alongside, as a browser does).</summary>
    private async Task<StepResult<RenewalStepData>> SelectPaymentMethodStepAsync(RenewalStepData data)
    {
        var region = RenewalPages.Region(data.PageContent);
        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"popt={Encode($"tmpt:region:{region}:payNow")}",
            $"plopt={Encode($"tmpt:region:{region}:bpay")}",
            .. Session(data),
            .. ValueChange($"tmpt:region:{region}:payLater", $"tmpt:region:{region}:payNow"),
        ]));
        if (!response.IsSuccessStatusCode || !RenewalPages.OffersPayNow(content)) return Fail("Failed to select payment method", "Select Payment Method");

        data.PageContent = content;
        return Ok(data);
    }

    private async Task<StepResult<RenewalStepData>> OpenPaymentGatewayStepAsync(RenewalStepData data)
    {
        var region = RenewalPages.Region(data.PageContent);
        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            $"popt={Encode($"tmpt:region:{region}:payNow")}",
            .. Session(data),
            RenderRegion,
            .. Click($"tmpt:region:{region}:nextButt"),
        ]));
        if (!response.IsSuccessStatusCode || RenewalPages.PaymentUrl(content) is not { Length: > 0 } paymentUrl)
            return Fail("Failed to open payment gateway", "Open Payment Gateway");

        data.PageContent = content;
        data.PaymentUrl = paymentUrl;
        return Ok(data);
    }

    /// <summary>
    /// Pays on ASIC's payment gateway, then tells the renewal page it was paid. From here on a
    /// failure may mean ASIC holds the money: only ASIC's own "declined" is a definite no.
    /// </summary>
    private async Task<StepResult<RenewalStepData>> ProcessPaymentStepAsync(RenewalStepData data)
    {
        var reference = data.TransactionReference ?? "Unknown";
        var payment = await _paymentClient.ProcessPaymentAsync(data.PaymentUrl!, data.TransactionReference, data.CardDetails);
        if (!payment.Success)
            return Fail($"Renewal initiated but payment failed: {payment.Message}. Transaction reference: {reference}", "Process Payment");

        data.HostedTokenizationId = payment.HostedTokenizationId;

        var completion = await CompletePaymentAsync(data);
        if (completion.Success) return Ok(data);

        var declined = completion.Message.Contains("declined", StringComparison.OrdinalIgnoreCase);
        return Fail((declined
                        ? $"Card declined at ASIC: {completion.Message}. "
                        : $"Payment processed but completion failed: {completion.Message}. ") +
                    $"Transaction reference: {reference}",
            "Complete Payment Action");
    }

    /// <summary>The gateway's "payment success" event, posted back to the renewal page's Pay Now popup.</summary>
    private async Task<PaymentResult> CompletePaymentAsync(RenewalStepData data)
    {
        try
        {
            var region = RenewalPages.PayNowRegion(data.PageContent);
            var popup = $"tmpt:region:{region}:payNowPopup";
            var paymentSuccess =
                "%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22_custom%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E" +
                $"%3Ck+v%3D%22sessionId%22%3E%3Cs%3E{data.TransactionReference}%3C%2Fs%3E%3C%2Fk%3E" +
                $"%3Ck+v%3D%22SST%22%3E%3Cs%3E{data.HostedTokenizationId}%3C%2Fs%3E%3C%2Fk%3E" +
                "%3Ck+v%3D%22immediate%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EpaymentSuccessAction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E";

            var (response, content) = await PostAsync(data.AdfWindowId, Form(
            [
                $"popt={Encode($"tmpt:region:{region}:payNow")}",
                .. Session(data),
                $"oracle.adf.view.rich.DELTAS=%7B{Encode(popup)}%3D%7B_shown%3D%7D%2C{Encode($"tmpt:region:{region}:payNowInline")}%3D%7Bsource%3D%7D%7D",
                $"event={Encode(popup)}",
                $"event.{popup}={paymentSuccess}",
                $"oracle.adf.view.rich.PROCESS={Encode(popup)}",
            ]));
            return RenewalPages.Completion(content, response.IsSuccessStatusCode, data.HostedTokenizationId);
        }
        catch (Exception ex)
        {
            return PaymentResult.Failed($"Payment completion failed: {ex.Message}");
        }
    }

    /* ───────────── Pay later by BPAY ───────────── */

    /// <summary>
    /// On "Select Payment Preference": tick "Pay later", then "BPAY", and read the biller code
    /// and customer reference ASIC shows underneath. They're shown only on this page, so the
    /// run stops here, before anything is lodged, unless both were read.
    /// </summary>
    private async Task<StepResult<RenewalStepData>> SelectBpayStepAsync(RenewalStepData data)
    {
        var region = RenewalPages.Region(data.PageContent);
        var content = await PostPayLaterChoiceAsync(data, region, ValueChange($"tmpt:region:{region}:payLater", $"tmpt:region:{region}:payNow"));
        if (content == null) return Fail("Failed to choose Pay later", "Select BPAY");

        // Then the BPAY option under it, unless ASIC already shows the BPAY details.
        if (RenewalPages.ReadBpay(content) == null)
        {
            var subRegion = RenewalPages.Region(content);
            var after = await PostPayLaterChoiceAsync(data, subRegion, ValueChange($"tmpt:region:{subRegion}:bpay", $"tmpt:region:{subRegion}:invoice"));
            if (after != null) content = after;
        }

        if (RenewalPages.ReadBpay(content) is not { } bpay)
            return Fail("ASIC didn't show a BPAY biller code and customer reference, so nothing was lodged. " +
                        $"Page said: {RenewalPages.Snippet(content)}", "Select BPAY");

        data.BpayBillerCode = bpay.BillerCode;
        data.BpayReference = bpay.Reference;
        data.PageContent = content;
        return Ok(data);
    }

    /// <summary>The "Pay Later" button: ASIC lodges the renewal, to be paid by BPAY.</summary>
    private async Task<StepResult<RenewalStepData>> SubmitPayLaterStepAsync(RenewalStepData data)
    {
        var region = RenewalPages.Region(data.PageContent);
        var (response, content) = await PostAsync(data.AdfWindowId, Form(
        [
            .. PayLaterChoice(region),
            .. Session(data),
            RenderRegion,
            .. Click($"tmpt:region:{region}:nextButt"),
        ]));

        if (!response.IsSuccessStatusCode || RenewalPages.StillOffersPayLater(content))
            return Fail($"Pay later wasn't accepted (BPAY biller {data.BpayBillerCode}, reference {data.BpayReference}, " +
                        $"transaction {data.TransactionReference ?? "unknown"}). Check ASIC before paying. Page said: {RenewalPages.Snippet(content)}",
                "Submit Pay Later");

        data.PageContent = content;
        return Ok(data);
    }

    /// <summary>The payment preference radios with Pay later + BPAY chosen.</summary>
    private static string[] PayLaterChoice(string region) =>
        [$"popt={Encode($"tmpt:region:{region}:payLater")}", $"plopt={Encode($"tmpt:region:{region}:bpay")}"];

    /// <summary>Posts a change on the payment preference page with Pay later + BPAY chosen; null on an HTTP failure.</summary>
    private async Task<string?> PostPayLaterChoiceAsync(RenewalStepData data, string region, string[] change)
    {
        var (response, content) = await PostAsync(data.AdfWindowId, Form([.. PayLaterChoice(region), .. Session(data), .. change]));
        return response.IsSuccessStatusCode ? content : null;
    }
}
