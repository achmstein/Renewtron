using System.Net;
using System.Text.RegularExpressions;
using Asic.Integrations.Payment;

namespace Asic.Integrations.Renewal;

/// <summary>ASIC's refusal to start a renewal, as the customer should read it, and the step it is reported at.</summary>
public sealed record RenewalRefusal(string Message, string Step);

/// <summary>
/// How Renewtron reads ASIC's public renewal pages (asicconnect.asic.gov.au/public/faces/renewal).
/// ASIC answers each step with an ADF partial response: XML whose HTML sits in CDATA sections.
/// These are read as raw text on purpose: an HTML parser drops CDATA, and with it the content.
/// Each marker and pattern was confirmed against live pages; change them only with a page to test on.
/// </summary>
public static partial class RenewalPages
{
    /* ───────────── Which page is this? ───────────── */

    /// <summary>The list of business names under the ABN.</summary>
    public static bool IsNameList(string content) => content.Contains("Business name to be renewed");

    /// <summary>
    /// ASIC accepted the ABN: the name list, or (resuming an earlier session) the email or
    /// payment preference page.
    /// </summary>
    public static bool AcceptsAbn(string content) =>
        IsNameList(content) || IsEmailPage(content) || IsPaymentPreference(content);

    /// <summary>A page with ASIC's "Next" button: a choice was taken.</summary>
    public static bool HasNextButton(string content) => content.Contains("buttonNextNew");

    public static bool IsRenewalPeriod(string content) => content.Contains("Renewal period");

    public static bool IsReview(string content) => content.Contains("Review");

    public static bool IsEmailPage(string content) => content.Contains("Email required for online payment");

    /// <summary>ASIC resumed an earlier session and went straight to the email page.</summary>
    public static bool IsResumedAtEmail(string content) =>
        content.Contains("Please provide a valid email which is required for ASIC online payment.");

    public static bool IsPaymentPreference(string content) => content.Contains("Select Payment Preference");

    public static bool OffersPayNow(string content) => content.Contains("Pay Now");

    /// <summary>Still on the preference page with its Pay Later button: ASIC didn't take the pay-later.</summary>
    public static bool StillOffersPayLater(string content) => content.Contains(">Pay Later</button>");

    /* ───────────── What does it say? ───────────── */

    /// <summary>ASIC won't start a renewal for this ABN, and why; null when it will.</summary>
    public static RenewalRefusal? Refusal(string content)
    {
        if (content.Contains("We are processing your renewal application"))
            return new("A renewal for this ABN is already in progress. " +
                       "Please complete or cancel the existing renewal before starting a new one. " +
                       "Check your email for the invoice or wait 48 hours for processing to complete.",
                       "Already Processing");

        if (content.Contains("This business name registration is not due for renewal"))
            return new("This business name is not due for renewal yet. " +
                       "ASIC will send a renewal notice when it becomes due. " +
                       "Check the business name register to see the next renewal date.",
                       "Not Due For Renewal");

        return null;
    }

    /// <summary>
    /// The names on the list. Each row is three dataText spans: name (HTML-encoded), account
    /// number, registration date. A trailing partial row is ignored.
    /// </summary>
    public static List<BusinessName> Names(string content)
    {
        var cells = DataTextRe().Matches(content);
        var names = new List<BusinessName>();
        for (var i = 0; i + 2 < cells.Count; i += 3)
        {
            names.Add(new BusinessName
            {
                Name = WebUtility.HtmlDecode(cells[i].Groups[1].Value),
                AccountNumber = cells[i + 1].Groups[1].Value,
                RegistrationDate = cells[i + 2].Groups[1].Value,
            });
        }
        return names;
    }

    /// <summary>The radio id of the row whose account number is <paramref name="accountNumber"/>; null when no row has it.</summary>
    public static string? RadioFor(string content, string accountNumber)
    {
        var radios = NameRadioRe().Matches(content);
        var cells = DataTextRe().Matches(content);
        for (var i = 0; i < radios.Count && i * 3 + 1 < cells.Count; i++)
            if (cells[i * 3 + 1].Groups[1].Value.Trim() == accountNumber)
                return radios[i].Groups[1].Value;
        return null;
    }

    /// <summary>ASIC's transaction reference, shown once the renewal is started; null when the page doesn't show it.</summary>
    public static string? TransactionReference(string content) =>
        TransactionRe().Match(content) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// The ADF region the page's components live in (tmpt:region:N:...): ASIC numbers regions
    /// afresh between pages, so each post must use the current one. "0" when none is shown.
    /// </summary>
    public static string Region(string? content) =>
        RegionRe().Match(content ?? "") is { Success: true } m ? m.Groups[1].Value : "0";

    /// <summary>The region hosting the Pay Now popup (or Pay Now itself); "3" when the page shows neither.</summary>
    public static string PayNowRegion(string? content)
    {
        if (string.IsNullOrEmpty(content)) return "3";
        if (PayNowPopupRe().Match(content) is { Success: true } popup) return popup.Groups[1].Value;
        if (PayNowRe().Match(content) is { Success: true } payNow) return payNow.Groups[1].Value;
        return "3";
    }

    /// <summary>The ASIC payment page the Pay Now button opens; null when the page doesn't give one.</summary>
    public static string? PaymentUrl(string content) =>
        PaymentUrlRe().Match(content) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>"Biller Code: 17301" and "Customer Reference Number: 8596799759492", with or without tags between.</summary>
    public static (string BillerCode, string Reference)? ReadBpay(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var biller = BillerRe().Match(html);
        var reference = ReferenceRe().Match(html);
        return biller.Success && reference.Success ? (biller.Groups[1].Value, reference.Groups[1].Value) : null;
    }

    /// <summary>
    /// What ASIC's renewal page says after the payment gateway hands back: paid (Next disabled,
    /// or on to Confirm), declined (still offering Pay Now, with ASIC's reason when it gives one),
    /// or unknown. An unreadable page that came back 200 counts as paid.
    /// </summary>
    public static PaymentResult Completion(string content, bool httpOk, string? hostedTokenizationId)
    {
        if (DisabledNextRe().IsMatch(content)) return PaymentResult.Succeeded(hostedTokenizationId);
        if (ConfirmStepRe().IsMatch(content) || content.Contains("paymentSuccessRedirect")) return PaymentResult.Succeeded(hostedTokenizationId);

        if (content.Contains(">Pay Now</button>"))
            return FirstParagraphRe().Match(content) is { Success: true } reason
                ? PaymentResult.Failed(LineBreakRe().Replace(reason.Groups[1].Value.Trim(), " ").Trim())
                : PaymentResult.Failed("Payment was declined or an error occurred");

        return httpOk ? PaymentResult.Succeeded(hostedTokenizationId) : PaymentResult.Failed("Unknown payment status");
    }

    /// <summary>The page as plain text, cut to 300 characters: for error messages.</summary>
    public static string Snippet(string? html)
    {
        var text = WhitespaceRe().Replace(TagRe().Replace(html ?? "", " "), " ").Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }

    private const string Gap = @"(?:\s|&nbsp;|&#160;|<[^>]*>)*";

    [GeneratedRegex(@"<span class=""dataText"">([^<]+)</span>")] private static partial Regex DataTextRe();
    [GeneratedRegex(@"<span id=""(tmpt:region:\d+:form:sbr[^""]+)""[^>]*class=""selectBooleanRadioHiddenLabel")] private static partial Regex NameRadioRe();
    [GeneratedRegex(@"Transaction reference number:<\/strong>\s*([A-Z0-9\-]+)")] private static partial Regex TransactionRe();
    [GeneratedRegex(@"tmpt:region:(\d+):[^:]*")] private static partial Regex RegionRe();
    [GeneratedRegex(@"tmpt:region:(\d+):payNowPopup")] private static partial Regex PayNowPopupRe();
    [GeneratedRegex(@"tmpt:region:(\d+):payNow\b")] private static partial Regex PayNowRe();
    [GeneratedRegex(@"'url':'(https://regpayment\.asic\.gov\.au/AsicPayment/faces/index\.jspx\?[^']+)'")] private static partial Regex PaymentUrlRe();
    [GeneratedRegex(@"Biller\s*Code\s*:?" + Gap + @"(\d{3,10})", RegexOptions.IgnoreCase)] private static partial Regex BillerRe();
    [GeneratedRegex(@"Customer\s*Reference\s*Number\s*:?" + Gap + @"(\d{6,20})", RegexOptions.IgnoreCase)] private static partial Regex ReferenceRe();
    [GeneratedRegex(@"<button[^>]*disabled[^>]*>Next</button>", RegexOptions.IgnoreCase)] private static partial Regex DisabledNextRe();
    [GeneratedRegex(@"class=""[^""]*stepOn[^""]*""[^>]*>[^<]*Confirm", RegexOptions.IgnoreCase)] private static partial Regex ConfirmStepRe();
    [GeneratedRegex(@"<p>(.*?)</p>", RegexOptions.Singleline)] private static partial Regex FirstParagraphRe();
    [GeneratedRegex(@"<br\s*/?>")] private static partial Regex LineBreakRe();
    [GeneratedRegex("<[^>]+>")] private static partial Regex TagRe();
    [GeneratedRegex(@"\s+")] private static partial Regex WhitespaceRe();
}
