#nullable enable
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Asic.Client;

/// <summary>How long a new business name is registered for (the Name page's alignmentPeriod radio).</summary>
public enum BnRegistrationPeriod
{
    OneYear = 0,
    ThreeYears = 1,
}

/// <summary>What ASIC's "Check Name Availability" said about a proposed name.</summary>
public enum BnNameVerdict
{
    Unknown,
    /// <summary>"… is currently available."</summary>
    Available,
    /// <summary>"… will be referred for a manual decision": ASIC lodges it without payment and decides later.</summary>
    ManualDecision,
    /// <summary>"Name is not available … identical or nearly identical … will be rejected."</summary>
    NotAvailable,
}

/// <summary>How we pay ASIC's fee on the Payment page.</summary>
public enum AsicPayBy
{
    /// <summary>"Pay now by credit card": the page hands us a regpayment.asic.gov.au window to pay in this session.</summary>
    Card,
    /// <summary>"Pay later" → BPAY: ASIC's biller code and a customer reference to pay within 10 business days.</summary>
    Bpay,
}

/// <summary>How far a registration got.</summary>
public enum BnRegistrationStage
{
    /// <summary>Stopped before anything was lodged (the draft was cancelled). See Problem.</summary>
    NotLodged,
    /// <summary>A dry run: everything filled in and reviewed, then cancelled at Review.</summary>
    DryRunOk,
    /// <summary>Lodged; pay ASIC's card window at PaymentUrl now, in this same session.</summary>
    AwaitingCardPayment,
    /// <summary>Lodged to pay later by BPAY (biller code + reference), within 10 business days.</summary>
    AwaitingBpay,
    /// <summary>Lodged with no payment asked for (a name referred for a manual decision).</summary>
    LodgedForReview,
    /// <summary>We pressed the button that lodges, but couldn't read what ASIC answered. Look in myTransactions.</summary>
    LodgedUnclear,
}

/// <summary>An application to register a business name, in the words ASIC's wizard needs.</summary>
public sealed record BnRegistrationRequest(
    string Abn,
    string BusinessName,
    BnRegistrationPeriod Period,
    AsicPortalClient.HolderAddress ServiceAddress,
    AsicPortalClient.HolderAddress PrincipalAddress,
    string Email,
    string? Mobile,
    AsicPayBy PayBy,
    bool AllowManualDecision);

/// <summary>What a registration run gave.</summary>
public sealed record BnRegistrationOutcome(
    BnRegistrationStage Stage,
    string? TransactionReference,
    string? AbrEntityName,
    string? AbrEntityType,
    BnNameVerdict Verdict,
    string? VerdictText,
    string? PaymentUrl,
    string? BpayBillerCode,
    string? BpayReference,
    string? Problem,
    string? FailedAtStep,
    string PageText)
{
    public bool Lodged => Stage is BnRegistrationStage.AwaitingCardPayment or BnRegistrationStage.AwaitingBpay
        or BnRegistrationStage.LodgedForReview or BnRegistrationStage.LodgedUnclear;
}

/// <summary>A row of the myTransactions table on the Lodgements &amp; Notifications tab.</summary>
public sealed record AsicTransactionRow(string Reference, string Description, string TransactionStatus, string PaymentStatus);

public sealed partial class AsicPortalClient
{
    /* ───────────── Registering a business name (confirmed live, 11–12 Nov 2026) ───────────── */

    public const string RegistrationPath = "emeraldCONNECT/faces/businessNamesRegistration";

    /// <summary>The ADF payload of an ADF table's first fetch (its rows load after the region does).</summary>
    public const string TableFetchPayload =
        "<m xmlns=\"http://oracle.com/richClient/comm\"><k v=\"id\"><n>0</n></k><k v=\"subtype\"><n>0</n></k>" +
        "<k v=\"index\"><n>-1</n></k><k v=\"renderOnly\"><b>1</b></k><k v=\"type\"><s>fetch</s></k></m>";

    /// <summary>The wizard's field and button ids, as served on 11 Nov 2026.</summary>
    public static class RegForm
    {
        public const string Choice = "regCreateChoice";
        public const string ChoiceBusinessName = "0";
        public const string ChoiceGo = "regCmdBut";
        public static readonly string[] StartBoxes = ["sbc1", "sbc2", "sbc3", "sbc4", "sbc5", "sbc6"];
        public const string GetStarted = "cb1";
        public const string AbnRadioGroup = "RadioButtons";
        public const string HasAbn = "sbr_24";
        public const string Abn = "inp_ABN";
        public const string AbnNext = "cbNext";
        public const string Name = "name:ProposedBusinessNameInputText";
        public const string CheckName = "name:button_check_1";
        public const string Period = "name:alignmentPeriod";
        public const string NameNext = "name:button_next_1";
        public const string BusinessNext = "bind:button_next_2";
        public const string AddressTable = "tblAddr";
        public const string AddressesNext = "button_next_3";
        public const string Eligibility = "elig:eligibilityOption1";
        public const string EligibilityNext = "elig:button_review_4";
        public const string ReviewSubmit = "review:button_validate_5";
        public const string ReviewCancel = "review:button_cancel_5";
        public static readonly string[] DeclareBoxes = ["declare:declaration1", "declare:declaration2", "declare:authority3", "declare:addBnToAccountID"];
        public const string DeclareNext = "declare:button_next_6";
        public const string PayGroup = "paymentPreferenceOptions";
        public const string PayNow = "payment:payNowOptionBoolean";
        public const string PayLater = "payment:payLaterOptionBoolean";
        public const string PayLaterGroup = "paymentPreferenceOption";
        public const string PayLaterBpay = "payment:payNowOption1";
        public const string PayNext = "payment:payment_NextBtn_7";
    }

    /// <summary>
    /// Registers a business name through ASIC Connect's "Apply for a Business Name" wizard, the way a
    /// person does it: start → six "things to be aware of" boxes → ABN → name (checked) and period →
    /// business → addresses (service, principal place, email, SMS) → eligibility → review → declare
    /// (on behalf of the applicant, added to our account) → payment preference.
    ///
    /// Nothing is lodged until Declare's Next: up to Review, any problem cancels the draft. With
    /// <paramref name="lodge"/> false the run stops at Review and cancels (a dry run). Reaching the
    /// Payment page is already a lodged application ("Registration Pending Payment"); a card payment
    /// must then be made at <see cref="BnRegistrationOutcome.PaymentUrl"/> before this session ends.
    /// </summary>
    public async Task<BnRegistrationOutcome> RegisterBusinessNameAsync(BnRegistrationRequest request, bool lodge, CancellationToken ct = default)
    {
        string? reference = null, entity = null, entityType = null, verdictText = null;
        var verdict = BnNameVerdict.Unknown;
        BnRegistrationOutcome NotLodged(string step, string problem, string text = "") =>
            new(BnRegistrationStage.NotLodged, reference, entity, entityType, verdict, verdictText, null, null, null, problem, step, text);

        var abn = Regex.Replace(request.Abn ?? "", @"\D", "");
        var name = Regex.Replace(request.BusinessName ?? "", @"\s+", " ").Trim();
        if (abn.Length != 11) return NotLodged("ABN", "The ABN must be 11 digits.");
        if (name.Length == 0) return NotLodged("Name", "No business name given.");

        // Start: Licences & Registrations → "Business name registration" → Go.
        var start = await OpenAsync(RegistrationPath, ct);
        if (start is null) return NotLodged("Start", "The ASIC Connect session ended.");
        var values = new Dictionary<string, string> { [RegForm.Choice] = RegForm.ChoiceBusinessName };
        var (page, _) = await PostEventAsync(start, RegForm.Choice, values, ValueChangePayload, ct: ct);
        var intro = await ClickAndFollowAsync(page, RegForm.ChoiceGo, values, ct: ct);
        if (intro is null) return NotLodged("Start", "\"Business name registration\" didn't open the application.", PageText(page.Html));

        // "Before you start": each box is an autoSubmit; Get Started stays disabled until all six are ticked.
        page = intro;
        values = [];
        foreach (var box in RegForm.StartBoxes)
        {
            values[box] = "t";
            (page, _) = await PostEventAsync(page, box, values, ValueChangePayload, ct: ct);
        }
        var abnPage = await ClickAndFollowAsync(page, RegForm.GetStarted, values, ct: ct);
        if (abnPage is null) return NotLodged("Start", "\"Get Started\" didn't open the ABN page.", PageText(intro.Html));

        // ABN: "has an ABN" (autoSubmit radio), the ABN, then Next posts the whole page.
        values = new() { [RegForm.AbnRadioGroup] = RegForm.HasAbn };
        (page, _) = await PostEventAsync(abnPage, RegForm.HasAbn, values, ValueChangePayload, ct: ct);
        values[RegForm.Abn] = abn;
        var wizard = await SubmitPageAsync(page, RegForm.AbnNext, values, ct);
        if (wizard is null) return NotLodged("ABN", "The ASIC Connect session ended.");
        if (!wizard.Url.Contains("lodgeBnComp", StringComparison.OrdinalIgnoreCase))
            return NotLodged("ABN", AbnProblem(PageText(wizard.Html)), PageText(wizard.Html));

        var html = wizard.Html;
        (reference, entity, entityType) = (ReadTransactionReference(html), ReadAbrField(html, "ABR Entity Name"), ReadAbrField(html, "Type"));
        page = wizard;

        // Name: check it, then the period.
        values = new() { [RegForm.Name] = name };
        var (afterCheck, check) = await PostEventAsync(page, RegForm.CheckName, values, process: $"name,{RegForm.CheckName}", ct: ct);
        page = afterCheck;
        (verdict, verdictText) = ReadNameVerdict(check);
        if (verdict is BnNameVerdict.NotAvailable or BnNameVerdict.Unknown
            || (verdict == BnNameVerdict.ManualDecision && !request.AllowManualDecision))
        {
            await CancelRegistrationAsync(page, check, ct);
            return NotLodged("Name", verdict switch
            {
                BnNameVerdict.NotAvailable => "ASIC says the name isn't available.",
                BnNameVerdict.ManualDecision => "ASIC would refer the name for a manual decision, and manual decisions aren't allowed.",
                _ => "ASIC's name check didn't say whether the name is available.",
            }, PartialText(check));
        }

        values[RegForm.Period] = ((int)request.Period).ToString();
        (page, _) = await PostEventAsync(page, RegForm.Period, values, ValueChangePayload, ct: ct);
        var (afterName, business) = await PostEventAsync(page, RegForm.NameNext, values, process: $"name,{RegForm.NameNext}", ct: ct);
        page = afterName;
        if (!business.Contains(RegForm.BusinessNext, StringComparison.Ordinal))
        {
            await CancelRegistrationAsync(page, check, ct);
            return NotLodged("Name", "The name page didn't move on to the business details.", PartialText(business));
        }

        // Business: for a company it only shows the ACN; other holder types may ask for more.
        var (afterBusiness, addresses) = await PostEventAsync(page, RegForm.BusinessNext, process: $"bind,{RegForm.BusinessNext}", ct: ct);
        page = afterBusiness;
        if (!addresses.Contains(RegForm.AddressesNext, StringComparison.Ordinal))
        {
            await CancelRegistrationAsync(page, business, ct);
            return NotLodged("Business", "The business page wants details we don't fill in yet (holder type: " + (entityType ?? "?") + ").",
                PartialText(addresses));
        }

        // Addresses: the table's rows load with a fetch; each row's Add opens its form below.
        var (rowsPage, rows) = await FetchAddressRowsAsync(page, ct);
        page = rowsPage;
        string? Row(string words) => rows.FirstOrDefault(r => r.Text.Contains(words, StringComparison.OrdinalIgnoreCase)).Row;
        var serviceRow = Row("Address for service");
        var principalRow = Row("Principal place");
        var emailRow = Row("Email");
        var smsRow = Row("SMS");
        if (serviceRow is null || principalRow is null || emailRow is null)
        {
            await CancelRegistrationAsync(page, addresses, ct);
            return NotLodged("Addresses", "The address table wasn't as expected.", string.Join(" | ", rows.Select(r => r.Text)));
        }

        page = await AddRowAsync(page, emailRow, ct);
        (page, var emailAnswer) = await PostEventAsync(page, "saveEmailBtn", new Dictionary<string, string> { ["emailInput"] = request.Email.Trim() },
            process: "view,saveEmailBtn", ct: ct);
        if (!string.IsNullOrWhiteSpace(request.Mobile) && smsRow is not null)
        {
            page = await AddRowAsync(page, smsRow, ct);
            (page, _) = await PostEventAsync(page, "saveSmsBtn", new Dictionary<string, string> { ["smsInput"] = Regex.Replace(request.Mobile, @"[^\d+]", "") },
                process: "view,saveSmsBtn", ct: ct);
        }

        string? addressProblem;
        Dictionary<string, string> formValues;
        (page, addressProblem, formValues) = await SaveRegistrationAddressAsync(page, serviceRow, request.ServiceAddress, ct);
        if (addressProblem is null)
            (page, addressProblem, formValues) = await SaveRegistrationAddressAsync(page, principalRow, request.PrincipalAddress, ct);

        (page, rows) = await FetchAddressRowsAsync(page, ct);
        var shown = string.Join(" | ", rows.Select(r => r.Text));
        addressProblem ??= !shown.Contains(request.Email.Trim(), StringComparison.OrdinalIgnoreCase) ? "ASIC didn't keep the email."
            : !RowShows(rows, "Address for service", request.ServiceAddress) ? "ASIC didn't keep the address for service."
            : !RowShows(rows, "Principal place", request.PrincipalAddress) ? "ASIC didn't keep the principal place of business."
            : null;
        if (addressProblem is not null)
        {
            await CancelRegistrationAsync(page, addresses, ct);
            return NotLodged("Addresses", addressProblem, shown + " " + PartialText(emailAnswer));
        }

        // Eligibility → Review.
        // Next posts the address form too: send it as the browser has it (the last address still
        // filled in). Sent with the page's original empty fields, ASIC wiped the principal place.
        formValues.Remove("radioQASAddress");
        var (afterAddresses, eligibility) = await PostEventAsync(page, RegForm.AddressesNext, formValues, process: $"view,{RegForm.AddressesNext}", ct: ct);
        page = afterAddresses;
        if (!eligibility.Contains(RegForm.EligibilityNext, StringComparison.Ordinal))
        {
            await CancelRegistrationAsync(page, addresses, ct);
            return NotLodged("Addresses", "The addresses page didn't move on to eligibility.", PartialText(eligibility));
        }
        values = new() { [RegForm.Eligibility] = "t" };
        var (afterElig, review) = await PostEventAsync(page, RegForm.EligibilityNext, values, process: $"elig,{RegForm.EligibilityNext}", ct: ct);
        page = afterElig;
        var reviewText = PartialText(review);
        if (!review.Contains(RegForm.ReviewSubmit, StringComparison.Ordinal) || !reviewText.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            await CancelRegistrationAsync(page, eligibility, ct);
            return NotLodged("Review", "The review page didn't show the application.", reviewText);
        }
        var reviewProblem = ReadAsicFee(reviewText) is null ? "The review page didn't show ASIC's fee."
            : !ReviewShowsAddress(reviewText, "Address for service of documents", request.ServiceAddress) ? "The review page doesn't show the address for service."
            : !ReviewShowsAddress(reviewText, "Principal place of business/practice", request.PrincipalAddress) ? "The review page doesn't show the principal place of business."
            : !reviewText.Contains(request.Email.Trim(), StringComparison.OrdinalIgnoreCase) ? "The review page doesn't show the email."
            : null;
        if (reviewProblem is not null)
        {
            await CancelRegistrationAsync(page, review, ct);
            return NotLodged("Review", reviewProblem, reviewText);
        }

        if (!lodge)
        {
            await CancelRegistrationAsync(page, review, ct);
            return new(BnRegistrationStage.DryRunOk, reference, entity, entityType, verdict, verdictText, null, null, null, null, null, reviewText);
        }

        // Review's Submit opens Declare. ASIC's web tier sometimes answers it with a "Weblogic
        // Bridge" error page while the server moves on anyway (seen 11 Nov 2026), so Declare's own
        // answer is what we check.
        var (onDeclare, _) = await PostEventAsync(page, RegForm.ReviewSubmit, process: $"review,{RegForm.ReviewSubmit}", ct: ct);
        page = onDeclare;

        // Declare's Next lodges the application. From here on, every answer is "lodged".
        values = RegForm.DeclareBoxes.ToDictionary(b => b, _ => "t");
        var (onPayment, payment) = await PostEventAsync(page, RegForm.DeclareNext, values, process: $"declare,{RegForm.DeclareNext}", ct: ct);
        page = onPayment;
        var paymentText = PartialText(payment);
        reference = ReadTransactionReference(payment) ?? reference;
        BnRegistrationOutcome Lodged(BnRegistrationStage stage, string text, string? url = null, string? biller = null, string? crn = null, string? problem = null) =>
            new(stage, reference, entity, entityType, verdict, verdictText, url, biller, crn, problem, problem is null ? null : "Payment", text);

        if (!payment.Contains(RegForm.PayNext, StringComparison.Ordinal))
        {
            return IsRegistrationConfirmation(paymentText)
                ? Lodged(BnRegistrationStage.LodgedForReview, paymentText)
                : Lodged(BnRegistrationStage.LodgedUnclear, paymentText, problem: "Declare didn't open the Payment page.");
        }

        if (request.PayBy == AsicPayBy.Card)
        {
            values = new() { [RegForm.PayGroup] = RegForm.PayNow };
            (page, _) = await PostEventAsync(page, RegForm.PayNow, values, ValueChangePayload, ct: ct);
            var (_, opened) = await PostEventAsync(page, RegForm.PayNext, values, process: $"payment,{RegForm.PayNext}", ct: ct);
            var url = ReadPaymentUrl(opened);
            return url is null
                ? Lodged(BnRegistrationStage.LodgedUnclear, PartialText(opened), problem: "ASIC didn't open its card payment window.")
                : Lodged(BnRegistrationStage.AwaitingCardPayment, paymentText, url);
        }

        var bpay = await PayLaterByBpayAsync(page, ct);
        return Lodged(BnRegistrationStage.AwaitingBpay, bpay.Text, biller: bpay.Biller, crn: bpay.Reference, problem: bpay.Problem) with
        {
            Stage = bpay.Biller is null ? BnRegistrationStage.LodgedUnclear : BnRegistrationStage.AwaitingBpay,
        };
    }

    /// <summary>
    /// On the Payment page: "Pay later" → BPAY → read the biller code and reference → Next (ASIC's
    /// "held, but will not be registered until you pay" page).
    /// </summary>
    private async Task<(string? Biller, string? Reference, string? Problem, string Text)> PayLaterByBpayAsync(AsicPortalPage page, CancellationToken ct)
    {
        var values = new Dictionary<string, string> { [RegForm.PayGroup] = RegForm.PayLater };
        (page, _) = await PostEventAsync(page, RegForm.PayLater, values, ValueChangePayload, ct: ct);
        values[RegForm.PayLaterGroup] = RegForm.PayLaterBpay;
        var (afterBpay, bpay) = await PostEventAsync(page, RegForm.PayLaterBpay, values, ValueChangePayload, ct: ct);
        var (biller, crn) = ReadBpayDetails(PartialText(bpay));
        if (biller is null || crn is null)
            return (null, null, "ASIC didn't show the BPAY biller code and reference.", PartialText(bpay));
        var (_, done) = await PostEventAsync(afterBpay, RegForm.PayNext, values, process: $"payment,{RegForm.PayNext}", ct: ct);
        var doneText = PartialText(done);
        return IsRegistrationConfirmation(doneText)
            ? (biller, crn, null, doneText)
            : (biller, crn, "ASIC showed the BPAY details but not its confirmation page.", doneText);
    }

    /// <summary>
    /// A lodged application whose card payment didn't go through ("Validated", payment "declined"):
    /// myTransactions → its Resume (lands on Declare) → Declare's Next → Payment → pay later by BPAY.
    /// </summary>
    public async Task<(string? Biller, string? Reference, string? Problem, string Text)> ResumeAndPayLaterByBpayAsync(string reference, CancellationToken ct = default)
    {
        var list = await OpenLodgementsAsync(ct);
        if (list is null) return (null, null, "The ASIC Connect session ended.", "");
        var row = ParseTransactionRowId(list.Html, reference);
        if (row is null) return (null, null, $"{reference} isn't in myTransactions (or has no Resume).", "");
        var declare = await ClickAndFollowAsync(list, $"{row}:cl1", ct: ct);
        if (declare is null || !declare.Html.Contains(RegForm.DeclareNext, StringComparison.Ordinal))
            return (null, null, "Resume didn't open the Declare page.", declare is null ? "" : PageText(declare.Html));
        var values = RegForm.DeclareBoxes.ToDictionary(b => b, _ => "t");
        var (page, payment) = await PostEventAsync(declare, RegForm.DeclareNext, values, process: $"declare,{RegForm.DeclareNext}", ct: ct);
        if (!payment.Contains(RegForm.PayNext, StringComparison.Ordinal))
            return (null, null, "Declare didn't open the Payment page.", PartialText(payment));
        return await PayLaterByBpayAsync(page, ct);
    }

    /// <summary>The myTransactions row id ("viewtransactions:…:tbTrns:0") holding <paramref name="reference"/>.</summary>
    public static string? ParseTransactionRowId(string html, string reference)
    {
        var doc = new HtmlParser().ParseDocument(html);
        return doc.QuerySelectorAll("td[id$=':c5']")
            .Where(td => td.Id!.Contains("tbTrns", StringComparison.Ordinal))
            .FirstOrDefault(td => td.TextContent.Trim().Equals(reference, StringComparison.OrdinalIgnoreCase))
            ?.Id![..^3];
    }

    /// <summary>Lodgements &amp; Notifications → myTransactions: our most recent transactions and their payment status.</summary>
    public async Task<IReadOnlyList<AsicTransactionRow>?> GetRecentTransactionsAsync(CancellationToken ct = default)
    {
        var list = await OpenLodgementsAsync(ct);
        return list is null ? null : ParseTransactions(list.Html);
    }

    /// <summary>The address table's rows: row number and its words.</summary>
    private async Task<(AsicPortalPage Page, IReadOnlyList<(string Row, string Text)> Rows)> FetchAddressRowsAsync(AsicPortalPage page, CancellationToken ct)
    {
        var (after, response) = await PostEventAsync(page, RegForm.AddressTable, payload: TableFetchPayload, process: RegForm.AddressTable, ct: ct);
        return (after, ParseAddressRows(response));
    }

    private async Task<AsicPortalPage> AddRowAsync(AsicPortalPage page, string row, CancellationToken ct)
    {
        var add = $"{RegForm.AddressTable}:{row}:addLink";
        (page, _) = await PostEventAsync(page, add, process: $"view,{add}", ct: ct);
        return page;
    }

    /// <summary>
    /// A row's Add → Australia (→ street address, for the address for service) → the address →
    /// Save Address → ASIC's address check: its own version when it is the same place (same street
    /// number and postcode, and no unit or level we didn't give), else the address as entered.
    /// </summary>
    private async Task<(AsicPortalPage Page, string? Problem, Dictionary<string, string> Values)> SaveRegistrationAddressAsync(
        AsicPortalPage page, string row, HolderAddress address, CancellationToken ct)
    {
        page = await AddRowAsync(page, row, ct);
        var values = new Dictionary<string, string> { ["drpCountry"] = "Australia" };
        (page, _) = await PostEventAsync(page, "drpCountry", values, ValueChangePayload, ct: ct);
        // Both address forms carry the Address Type list; without "Street Address" the principal
        // place is dropped silently (Review showed it blank, 12 Nov 2026).
        values["drpServiceAddressType"] = "Physical Address";
        (page, _) = await PostEventAsync(page, "drpServiceAddressType", values, ValueChangePayload, ct: ct);
        values["txtStreetNum"] = address.StreetNumber.Trim();
        values["txtStreetName"] = address.StreetName.Trim();
        values["drpStreetType"] = address.StreetTypeCode.Trim();
        values["txtSuburbCity"] = address.Suburb.Trim();
        values["txtPostcode"] = address.Postcode.Trim();
        values["drpStateTerritory"] = address.State.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(address.Unit)) values["txtUnitOffice"] = address.Unit.Trim();

        var (afterSave, answer) = await PostEventAsync(page, "saveAddrBtn", values, process: "view,saveAddrBtn", ct: ct);
        page = afterSave;
        var choices = ParseQasChoices(answer, "radioQASAddress");
        if (choices.Count == 0)
        {
            // No address check popup: saved as entered, or refused with a message.
            var messages = ReadMessages(answer);
            return (page, messages.Count > 0 ? string.Join(" ", messages) : null, values);
        }
        var pick = PickQasChoice(choices, address);
        values["radioQASAddress"] = pick.Value;
        (page, _) = await PostEventAsync(page, "cmdConfirm", values, process: "popupSaveAddress,cmdConfirm", ct: ct);
        return (page, null, values);
    }

    /// <summary>Leaves the application without lodging: its region's Cancel, then "Yes".</summary>
    private async Task CancelRegistrationAsync(AsicPortalPage page, string lastAnswer, CancellationToken ct)
    {
        var cancel = CancelIdRe().Match(lastAnswer) is { Success: true } m ? m.Groups[1].Value
            : CancelIdRe().Match(page.Html) is { Success: true } p ? p.Groups[1].Value : null;
        if (cancel is null) return;
        await CancelWizardAsync(page, lastAnswer + page.Html, RegionOf(cancel), ct);
    }

    /* ───────────── Reading the registration pages (pure, unit tested) ───────────── */

    /// <summary>"Transaction reference number: 1-11AQR319".</summary>
    public static string? ReadTransactionReference(string html) =>
        TxnRefRe().Match(PartialText(html)) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>A value from the "Details On The Australian Business Register" box ("ABR Entity Name", "ABN", "Type").</summary>
    public static string? ReadAbrField(string html, string label)
    {
        var text = PartialText(html);
        var m = Regex.Match(text, $@"{Regex.Escape(label)}\s*:\s*(.+?)\s+(?:ABN\s*:|Type\s*:|Proposed|Business details|Asterisk|Company or|Fee details|Declaration|Addresses|Eligibility|Here are|Name Hide|$)",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>ASIC's verdict on a name, from the Check Name Availability answer.</summary>
    public static (BnNameVerdict Verdict, string? Text) ReadNameVerdict(string response)
    {
        var text = PartialText(response);
        // ASIC's words come after the "Check Name Availability" button.
        var at = text.LastIndexOf("Check Name Availability", StringComparison.OrdinalIgnoreCase);
        if (at >= 0) text = text[(at + "Check Name Availability".Length)..];
        var available = Regex.Match(text, @"\S[^.]*? is currently available\.", RegexOptions.IgnoreCase);
        if (available.Success) return (BnNameVerdict.Available, available.Value.Trim());
        var manual = Regex.Match(text, @"Name may not be available\..*?(?:This name may be rejected\.|$)", RegexOptions.IgnoreCase);
        if (manual.Success || text.Contains("referred for a manual decision", StringComparison.OrdinalIgnoreCase))
            return (BnNameVerdict.ManualDecision, manual.Success ? manual.Value.Trim() : "Referred for a manual decision.");
        var refused = Regex.Match(text, @"Name is not available\..*?(?:This name will be rejected\.|We suggest you try another name\.|$)", RegexOptions.IgnoreCase);
        if (refused.Success) return (BnNameVerdict.NotAvailable, refused.Value.Trim());
        return (BnNameVerdict.Unknown, null);
    }

    /// <summary>Why the ABN page didn't move on, in words a person can act on.</summary>
    public static string AbnProblem(string text)
    {
        if (text.Contains("restricted entity", StringComparison.OrdinalIgnoreCase))
            return "ASIC says this ABN belongs to a restricted entity and would queue the application for manual assessment.";
        var m = Regex.Match(text, @"(?:Error|Warning)\s*:\s*(.{10,240}?\.)", RegexOptions.IgnoreCase);
        return m.Success ? "ASIC didn't accept the ABN: " + m.Groups[1].Value.Trim() : "ASIC didn't accept the ABN.";
    }

    /// <summary>The address table's rows ("tblAddr:N"), each with its words.</summary>
    public static IReadOnlyList<(string Row, string Text)> ParseAddressRows(string response)
    {
        var rows = new List<(string, string)>();
        var html = response.Replace("<![CDATA[", " ").Replace("]]>", " ");
        // Rows hold nested tables, so a row runs from its own <tr _afrrk> to the next one's.
        var starts = AddressRowRe().Matches(html).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
            rows.Add((starts[i].Groups[1].Value, PageText(html[starts[i].Index..end])));
        }
        if (rows.Count > 0) return rows;

        // Fallback: split the words at each row's Add link.
        var adds = AddLinkRe().Matches(html).ToList();
        for (var i = 0; i < adds.Count; i++)
        {
            var from = i == 0 ? 0 : adds[i - 1].Index;
            rows.Add((adds[i].Groups[1].Value, PageText(html[from..adds[i].Index])));
        }
        return rows;
    }

    private static bool RowShows(IReadOnlyList<(string Row, string Text)> rows, string words, HolderAddress address)
    {
        var row = rows.FirstOrDefault(r => r.Text.Contains(words, StringComparison.OrdinalIgnoreCase)).Text ?? "";
        return row.Contains(address.Postcode.Trim(), StringComparison.OrdinalIgnoreCase)
               && row.Contains(address.StreetName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Review page's address table shows "&lt;type&gt; &lt;address&gt; Available to the public":
    /// true when the address after <paramref name="rowLabel"/> has our street name and postcode.
    /// </summary>
    public static bool ReviewShowsAddress(string reviewText, string rowLabel, HolderAddress address)
    {
        var m = Regex.Match(reviewText, $@"{Regex.Escape(rowLabel)}\s+(.*?)\s+(?:Not\s+)?Available to the public", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var shown = m.Groups[1].Value;
        return shown.Contains(address.StreetName.Trim(), StringComparison.OrdinalIgnoreCase)
               && Regex.IsMatch(shown, $@"\b{Regex.Escape(address.Postcode.Trim())}\b");
    }

    /// <summary>The choices on ASIC's "Confirm your address" popup: (value, words).</summary>
    public static IReadOnlyList<(string Value, string Label)> ParseQasChoices(string response, string radioName)
    {
        var html = response.Replace("<![CDATA[", " ").Replace("]]>", " ");
        var labels = Regex.Matches(html, $@"<label[^>]*for=""({Regex.Escape(radioName)}:_\d+)""[^>]*>(.*?)</label>", RegexOptions.Singleline)
            .GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => PageText(g.First().Groups[2].Value));
        return Regex.Matches(html, $@"<input[^>]*name=""{Regex.Escape(radioName)}""[^>]*id=""([^""]+)""[^>]*value=""([^""]*)""")
            .Select(m => (WebUtility.HtmlDecode(m.Groups[2].Value), labels.GetValueOrDefault(m.Groups[1].Value, "")))
            .ToList();
    }

    /// <summary>
    /// ASIC's own version of the address when it's the same place: it keeps our street number and
    /// postcode and adds no unit, shop, suite or level we didn't give (1 Martin Pl offers 130
    /// "Shop 1 …", "Level 7 …" versions). Otherwise the address as entered.
    /// </summary>
    public static (string Value, string Label) PickQasChoice(IReadOnlyList<(string Value, string Label)> choices, HolderAddress address)
    {
        bool Typed((string Value, string Label) c) => Regex.IsMatch(c.Label, @"entered address|as typed|enter as", RegexOptions.IgnoreCase);
        var number = address.StreetNumber.Trim();
        var unit = (address.Unit ?? "").Trim();
        var exact = choices.Where(c => !Typed(c))
            .Where(c => Regex.IsMatch(c.Label, $@"\b{Regex.Escape(address.Postcode.Trim())}\b"))
            .Where(c => c.Label.Contains(address.StreetName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c =>
            {
                var before = Regex.Match(c.Label.Trim(), $@"^(.*?)\b{Regex.Escape(number)}\s+{Regex.Escape(address.StreetName.Trim())}", RegexOptions.IgnoreCase);
                if (!before.Success) return false;
                var prefix = before.Groups[1].Value.Trim().TrimEnd('/', ',').Trim();
                return unit.Length == 0 ? prefix.Length == 0 : prefix.Contains(unit, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (exact.Count == 1) return exact[0];
        var typed = choices.Where(Typed).ToList();
        return typed.Count > 0 ? typed[0] : exact.Concat(choices).First();
    }

    /// <summary>ADF's error and warning messages in a partial answer.</summary>
    public static IReadOnlyList<string> ReadMessages(string response) =>
        Regex.Matches(response, @"AdfFacesMessage\(AdfFacesMessage\.TYPE_(?:ERROR|WARNING),'([^']*)','([^']*)'")
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}".Trim()).Distinct().ToList();

    /// <summary>"Registration Fee: $47.00 AUD" on the Review page.</summary>
    public static decimal? ReadAsicFee(string text) =>
        Regex.Match(text, @"Registration Fee:\s*\$\s*([\d,]+\.\d{2})", RegexOptions.IgnoreCase) is { Success: true } m
            && decimal.TryParse(m.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var fee)
            ? fee : null;

    /// <summary>The regpayment.asic.gov.au window the Payment page's Next opens.</summary>
    public static string? ReadPaymentUrl(string response) =>
        PaymentUrlRe().Match(response) is { Success: true } m ? WebUtility.HtmlDecode(m.Value) : null;

    /// <summary>"Biller Code: 17301 Customer Reference Number: 8597040845155".</summary>
    public static (string? Biller, string? Reference) ReadBpayDetails(string text)
    {
        var biller = Regex.Match(text, @"Biller Code:\s*(\d{3,})", RegexOptions.IgnoreCase);
        var crn = Regex.Match(text, @"Customer Reference Number:\s*(\d{6,})", RegexOptions.IgnoreCase);
        return (biller.Success ? biller.Groups[1].Value : null, crn.Success ? crn.Groups[1].Value : null);
    }

    /// <summary>The page after lodging: "You have successfully applied to register a business name".</summary>
    public static bool IsRegistrationConfirmation(string text) =>
        text.Contains("successfully applied to register", StringComparison.OrdinalIgnoreCase)
        || text.Contains("successfully lodged", StringComparison.OrdinalIgnoreCase);

    /// <summary>The myTransactions table: reference (c5), description (c2), transaction status (c3), payment status (c4).</summary>
    public static IReadOnlyList<AsicTransactionRow> ParseTransactions(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        string Cell(string row, string col) =>
            Regex.Replace(doc.GetElementById($"{row}:{col}")?.TextContent ?? "", @"\s+", " ").Trim();
        return doc.QuerySelectorAll("td[id$=':c5']")
            .Select(td => td.Id![..^3])
            .Where(row => row.Contains("tbTrns", StringComparison.Ordinal))
            .Select(row => new AsicTransactionRow(Cell(row, "c5"), Cell(row, "c2"), Cell(row, "c3"), Cell(row, "c4")))
            .Where(r => r.Reference.Length > 0)
            .ToList();
    }

    [GeneratedRegex(@"Transaction reference number\s*:?\s*(\d-[0-9A-Z]{5,})", RegexOptions.IgnoreCase)] private static partial Regex TxnRefRe();
    [GeneratedRegex(@"<tr[^>]*_afrrk=""(\d+)""[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex AddressRowRe();
    [GeneratedRegex(@"id=""tblAddr:(\d+):addLink""")] private static partial Regex AddLinkRe();
    [GeneratedRegex(@"https://regpayment\.asic\.gov\.au/AsicPayment/faces/index\.jspx\?[^'""<\s]+")] private static partial Regex PaymentUrlRe();
}
