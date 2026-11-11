using System.Net;
using Asic.Integrations.Payment;
using Asic.Integrations.Renewal;

namespace Renewtron.Tests.AsicClients;

/// <summary>
/// Characterisation tests for <see cref="AsicRenewalClient"/>: they pin what it does today
/// (every request it sends, and how it reads each answer) so the Asic.Integrations rewrite can
/// be checked against them. The pages are synthetic: just the markers the client looks for.
/// </summary>
public class AsicRenewalClientTests
{
    private const string Abn = "49642383462";

    /* ───────────── ASIC's pages, reduced to what the client reads ───────────── */

    private const string Landing = "<script>AdfPage.PAGE.foo({'_afrLoop', '4242'}); x('_afrPage', '', 'win0');</script>";
    private const string LoopbackPage = """<form><input type="hidden" name="Adf-Window-Id" value="w1"><input type="hidden" name="javax.faces.ViewState" value="!vs1"></form>""";

    private static string NamesPage(params (string Name, string Account, string Registered)[] rows) =>
        "<h2>Business name to be renewed</h2><table>" + string.Concat(rows.Select((r, i) =>
            $"""<tr><td><span id="tmpt:region:1:form:sbr{i}" class="selectBooleanRadioHiddenLabel"></span></td><td><span class="dataText">{r.Name}</span></td><td><span class="dataText">{r.Account}</span></td><td><span class="dataText">{r.Registered}</span></td></tr>""")) + "</table>";

    private static readonly string TwoNames = NamesPage(("BEAUTIQUE HAIR LOUNGE &amp; BEAUTY BAR", "123456789", "08/10/2020"), ("SECOND NAME", "987654321", "01/02/2019"));

    private const string NextButton = """<span id="tmpt:region:1:form:x"></span><button class="buttonNextNew">Next</button>""";
    private const string RenewalPeriodPage = "<p><strong>Transaction reference number:</strong> 7E1234567-AB</p><h3>Renewal period</h3>";
    private const string ReviewPage = "<h3>Review</h3>";
    private const string EmailPage = """<label>Email required for online payment</label><input id="tmpt:region:4:form:emIn1">""";
    private const string PreferencePage = """<h3>Select Payment Preference</h3><span id="tmpt:region:5:payNow"></span>""";
    private const string PayNowPage = """<span id="tmpt:region:5:popt"></span><button>Pay Now</button>""";
    private const string GatewayPage = """<span id="tmpt:region:6:payNowPopup"></span><script>openPopup({'url':'https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SessionId=S1&SST=T1'})</script>""";
    private const string ConfirmStep = """<li class="train stepOn">Confirm</li>""";
    private const string BpayShown = """<span id="tmpt:region:5:bpayInfo"></span><p>Biller Code: <b>17301</b></p><p>Customer Reference Number:&nbsp;<b>8596799759492</b></p>""";
    private const string PayLaterDone = """<span id="tmpt:region:5:done"></span><p>Your renewal has been lodged.</p>""";

    private static ScriptedHttp SessionOpens(ScriptedHttp http) =>
        http.Reply(Landing, setCookie: "JSESSIONID=sess123; Path=/").Reply(LoopbackPage);

    /* ───────────── Fakes ───────────── */

    private sealed class FakePayment(PaymentResult answer) : IAsicPaymentClient
    {
        public List<(string Url, string SessionId, CreditCardDetails Card)> Calls { get; } = [];
        public Task<PaymentResult> ProcessPaymentAsync(string paymentUrl, string sessionId, CreditCardDetails cardDetails)
        {
            Calls.Add((paymentUrl, sessionId, cardDetails));
            return Task.FromResult(answer);
        }
    }

    private static readonly CreditCardDetails Card = new()
    {
        CardNumber = "4111111111111111", CardholderName = "Test Holder", ExpiryMonth = "12", ExpiryYear = "2030", Cvc = "123",
    };

    private static (AsicRenewalClient Client, ScriptedHttp Http, FakePayment Payment, List<TimeSpan> Waits) Client(
        PaymentResult? payment = null)
    {
        var http = new ScriptedHttp();
        var pay = new FakePayment(payment ?? PaymentResult.Succeeded("HT-1"));
        var waits = new List<TimeSpan>();
        var client = new AsicRenewalClient(pay, new CookieContainer(), http, d => { waits.Add(d); return Task.CompletedTask; });
        return (client, http, pay, waits);
    }

    private static ScriptedHttp UpToPreferencePage(ScriptedHttp http) =>
        SessionOpens(http)
            .Reply(TwoNames)          // submit ABN
            .Reply(NextButton)        // select business
            .Reply(RenewalPeriodPage) // proceed to renewal period
            .Reply(NextButton)        // select period
            .Reply(ReviewPage)        // proceed to review
            .Reply(NextButton)        // authority declaration
            .Reply(EmailPage)         // proceed to payment
            .Reply(PreferencePage);   // submit email

    /* ───────────── Search ───────────── */

    [Fact]
    public async Task Search_lists_each_name_with_account_and_registration_date()
    {
        var (client, http, _, _) = Client();
        SessionOpens(http).Reply(TwoNames);

        var result = await client.SearchByAbnAsync(Abn);

        Assert.True(result.Success);
        Assert.Equal(["BEAUTIQUE HAIR LOUNGE & BEAUTY BAR", "SECOND NAME"], result.BusinessNames.Select(n => n.Name));
        Assert.Equal(["123456789", "987654321"], result.BusinessNames.Select(n => n.AccountNumber));
        Assert.Equal(["08/10/2020", "01/02/2019"], result.BusinessNames.Select(n => n.RegistrationDate));
        Golden.Verify("renewal-search", http.Transcript());
    }

    [Fact]
    public async Task Search_not_due_is_asics_answer_not_retried()
    {
        var (client, http, _, waits) = Client();
        SessionOpens(http).Reply("<p>This business name registration is not due for renewal</p>");

        var result = await client.SearchByAbnAsync(Abn);

        Assert.False(result.Success);
        Assert.Equal("This business name is not due for renewal yet. ASIC will send a renewal notice when it becomes due. Check the business name register to see the next renewal date.", result.ErrorMessage);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Search_already_processing_is_asics_answer()
    {
        var (client, http, _, _) = Client();
        SessionOpens(http).Reply("<p>We are processing your renewal application</p>");

        var result = await client.SearchByAbnAsync(Abn);

        Assert.Equal("A renewal for this ABN is already in progress. Please complete or cancel the existing renewal before starting a new one. Check your email for the invoice or wait 48 hours for processing to complete.", result.ErrorMessage);
    }

    [Fact]
    public async Task Search_unrecognised_page_asks_to_check_the_abn()
    {
        var (client, http, _, _) = Client();
        SessionOpens(http).Reply("<p>Something else</p>");

        var result = await client.SearchByAbnAsync(Abn);

        Assert.Equal("Failed to search for business names. Please check the ABN and try again.", result.ErrorMessage);
    }

    [Fact]
    public async Task Search_with_no_rows_reports_no_names()
    {
        var (client, http, _, _) = Client();
        SessionOpens(http).Reply("<h2>Business name to be renewed</h2>");

        var result = await client.SearchByAbnAsync(Abn);

        Assert.Equal("No business names found for this ABN", result.ErrorMessage);
    }

    [Fact]
    public async Task Search_network_failures_are_retried_three_times_then_reported()
    {
        var (client, http, _, waits) = Client();
        http.Throw("boom").Throw("boom").Throw("boom");

        var result = await client.SearchByAbnAsync(Abn);

        Assert.Equal("Search failed: boom", result.ErrorMessage);
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)], waits);
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Search_session_that_wont_open_is_retried_then_reported()
    {
        var (client, http, _, waits) = Client();
        for (var i = 0; i < 3; i++) http.Reply("maintenance").Reply("maintenance"); // no cookie, no window id

        var result = await client.SearchByAbnAsync(Abn);

        Assert.Equal("Failed to initialize renewal session", result.ErrorMessage);
        Assert.Equal(2, waits.Count);
    }

    [Fact]
    public async Task Search_recovers_when_a_retry_succeeds()
    {
        var (client, http, _, _) = Client();
        http.Throw("blip");
        SessionOpens(http).Reply(TwoNames);

        var result = await client.SearchByAbnAsync(Abn);

        Assert.True(result.Success);
    }

    /* ───────────── Renewal paid by card ───────────── */

    [Fact]
    public async Task Card_renewal_walks_every_step_and_pays_through_the_gateway()
    {
        var (client, http, payment, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage).Reply(ConfirmStep);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 3, "owner@example.com", Card);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(("7E1234567-AB", "HT-1"), (result.TransactionReference, result.HostedTokenizationId));
        var call = Assert.Single(payment.Calls);
        Assert.Equal("https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SessionId=S1&SST=T1", call.Url);
        Assert.Equal("7E1234567-AB", call.SessionId);
        Assert.Equal(0, http.Unused);
        Golden.Verify("renewal-card-3-years", http.Transcript());
    }

    [Fact]
    public async Task One_year_renewal_picks_the_first_period()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage).Reply(ConfirmStep);

        await client.RenewBusinessNameAsync(Abn, "987654321", 1, "owner@example.com", Card);

        Assert.Contains(http.Requests, r => r.Body.Contains("tmpt:region:2:form:selRen=0&"));
        Assert.Contains(http.Requests, r => r.Body.Contains("bngrp=tmpt%3Aregion%3A1%3Aform%3Asbr1&")); // second row
    }

    [Fact]
    public async Task Account_number_not_listed_stops_before_anything_is_chosen()
    {
        var (client, http, payment, _) = Client();
        SessionOpens(http).Reply(TwoNames);

        var result = await client.RenewBusinessNameAsync(Abn, "000000000", 1, "owner@example.com", Card);

        Assert.False(result.IsSuccess);
        Assert.Equal(("Could not find business name with account number 000000000", "Select Business Name"), (result.Message, result.FailedAtStep));
        Assert.Equal(3, http.Requests.Count);
        Assert.Empty(payment.Calls);
    }

    [Theory]
    [InlineData("<p>This business name registration is not due for renewal</p>", "Not Due For Renewal", "not due for renewal")]
    [InlineData("<p>We are processing your renewal application</p>", "Already Processing", "already in progress")]
    [InlineData("<p>Unexpected</p>", "Submit ABN", "Failed to submit ABN")]
    public async Task Asic_refusing_the_abn_names_the_step(string page, string step, string message)
    {
        var (client, http, _, _) = Client();
        SessionOpens(http).Reply(page);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal(step, result.FailedAtStep);
        Assert.Contains(message, result.Message);
    }

    [Fact]
    public async Task Resumed_session_jumps_from_renewal_period_to_email()
    {
        var (client, http, _, _) = Client();
        SessionOpens(http)
            .Reply(TwoNames).Reply(NextButton)
            .Reply("<p>Please provide a valid email which is required for ASIC online payment.</p><span id=\"tmpt:region:4:form:emIn1\"></span>")
            .Reply(PreferencePage).Reply(PayNowPage).Reply(GatewayPage).Reply(ConfirmStep);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(9, http.Requests.Count); // period, review, declaration and proceed-to-payment skipped
        Assert.Null(result.TransactionReference);
    }

    [Fact]
    public async Task Gateway_payment_failure_is_reported_at_process_payment()
    {
        var (client, http, _, _) = Client(PaymentResult.Failed("Card expired"));
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal("Process Payment", result.FailedAtStep);
        Assert.Equal("Renewal initiated but payment failed: Card expired. Transaction reference: 7E1234567-AB", result.Message);
    }

    [Fact]
    public async Task Decline_after_paying_is_a_definite_no()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage)
            .Reply("<div class=\"errorPanel\"><p>Your card was declined.<br/>Try another card.</p></div><button>Pay Now</button>");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal("Complete Payment Action", result.FailedAtStep);
        Assert.Equal("Card declined at ASIC: Your card was declined. Try another card.. Transaction reference: 7E1234567-AB", result.Message);
    }

    [Fact]
    public async Task Still_on_pay_now_without_a_reason_is_unclear()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage).Reply("<button>Pay Now</button>");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal("Complete Payment Action", result.FailedAtStep);
        Assert.StartsWith("Card declined at ASIC: Payment was declined or an error occurred.", result.Message);
    }

    [Fact]
    public async Task Completion_http_error_leaves_payment_unclear()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage).Reply("oops", HttpStatusCode.InternalServerError);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal("Complete Payment Action", result.FailedAtStep);
        Assert.Equal("Payment processed but completion failed: Unknown payment status. Transaction reference: 7E1234567-AB", result.Message);
    }

    [Theory]
    [InlineData("<button disabled=\"disabled\">Next</button>")]
    [InlineData("<script>paymentSuccessRedirect()</script>")]
    [InlineData("<p>anything else, HTTP 200</p>")] // today: an unreadable 200 counts as paid
    public async Task Completion_reads_as_paid(string page)
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply(GatewayPage).Reply(page);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.True(result.IsSuccess, result.Message);
    }

    [Fact]
    public async Task No_payment_url_from_asic_stops_before_paying()
    {
        var (client, http, payment, _) = Client();
        UpToPreferencePage(http).Reply(PayNowPage).Reply("<p>no url</p>");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal(("Failed to open payment gateway", "Open Payment Gateway"), (result.Message, result.FailedAtStep));
        Assert.Empty(payment.Calls);
    }

    [Theory]
    [InlineData(4, "Select Business Name", "Failed to select business name")]
    [InlineData(5, "Proceed to Renewal Period", "Unexpected page state")]
    [InlineData(6, "Select Renewal Period", "Failed to select renewal period")]
    [InlineData(7, "Proceed to Review", "Failed to proceed to review")]
    [InlineData(8, "Select Authority Declaration", "Failed to select authority declaration")]
    [InlineData(9, "Proceed to Payment", "Failed to proceed to payment")]
    [InlineData(10, "Submit Email", "Failed to submit email")]
    [InlineData(11, "Select Payment Method", "Failed to select payment method")]
    public async Task Each_step_names_itself_when_asic_answers_unexpectedly(int failAt, string step, string message) // failAt: the request ASIC answers wrongly
    {
        var pages = new[] { TwoNames, NextButton, RenewalPeriodPage, NextButton, ReviewPage, NextButton, EmailPage, PreferencePage, PayNowPage };
        var (client, http, payment, _) = Client();
        SessionOpens(http);
        for (var i = 3; i < failAt; i++) http.Reply(pages[i - 3]);
        http.Reply("<p>nothing expected here</p>");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal((message, step), (result.Message, result.FailedAtStep));
        Assert.Empty(payment.Calls);
    }

    [Fact]
    public async Task An_exception_mid_renewal_is_reported_not_thrown()
    {
        var (client, http, _, _) = Client();
        SessionOpens(http).Throw("connection reset");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card);

        Assert.Equal(("Renewal failed: connection reset", "Exception"), (result.Message, result.FailedAtStep));
    }

    /* ───────────── Renewal lodged to pay later by BPAY ───────────── */

    [Fact]
    public async Task Pay_later_reads_the_bpay_details_then_lodges()
    {
        var (client, http, payment, _) = Client();
        UpToPreferencePage(http).Reply(BpayShown).Reply(PayLaterDone);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card, payLater: true);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(("17301", "8596799759492", "7E1234567-AB"), (result.BpayBillerCode, result.BpayReference, result.TransactionReference));
        Assert.Equal("Lodged with ASIC to pay later by BPAY (biller 17301, reference 8596799759492)", result.Message);
        Assert.Empty(payment.Calls);
        Golden.Verify("renewal-bpay", http.Transcript());
    }

    [Fact]
    public async Task Pay_later_ticks_bpay_when_asic_doesnt_show_it_straight_away()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply("""<span id="tmpt:region:7:bpay"></span><p>Pay later options</p>""").Reply(BpayShown).Reply(PayLaterDone);

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card, payLater: true);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Contains("event.tmpt:region:7:bpay=", http.Requests[^2].Body);
    }

    [Fact]
    public async Task Without_bpay_details_nothing_is_lodged()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply("<p>Pay later options</p>").Reply("<p>still nothing</p>");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card, payLater: true);

        Assert.Equal("Select BPAY", result.FailedAtStep);
        Assert.StartsWith("ASIC didn't show a BPAY biller code and customer reference, so nothing was lodged. Page said: still nothing", result.Message);
        // Ten requests to reach the preference page, then the two radio clicks: the Pay Later button is never pressed.
        Assert.Equal(12, http.Requests.Count);
        Assert.Contains("event.tmpt:region:0:bpay=", http.Requests[^1].Body);
    }

    [Fact]
    public async Task Pay_later_not_accepted_says_to_check_asic_before_paying()
    {
        var (client, http, _, _) = Client();
        UpToPreferencePage(http).Reply(BpayShown).Reply("<p>Error</p><button>Pay Later</button>");

        var result = await client.RenewBusinessNameAsync(Abn, "123456789", 1, "owner@example.com", Card, payLater: true);

        Assert.Equal("Submit Pay Later", result.FailedAtStep);
        Assert.StartsWith("Pay later wasn't accepted (BPAY biller 17301, reference 8596799759492, transaction 7E1234567-AB). Check ASIC before paying.", result.Message);
    }

    [Theory]
    [InlineData("Biller Code: 17301 ... Customer Reference Number: 8596799759492", "17301", "8596799759492")]
    [InlineData("<td>Biller Code</td><td><b>17301</b></td><td>Customer Reference Number</td><td>8596799759492</td>", "17301", "8596799759492")]
    [InlineData("biller code:&#160;123 customer reference number:&nbsp;123456", "123", "123456")]
    public void Bpay_details_are_read_with_or_without_markup(string html, string biller, string reference) =>
        Assert.Equal((biller, reference), RenewalPages.ReadBpay(html));

    [Theory]
    [InlineData(null)]
    [InlineData("Biller Code: 17301")]
    [InlineData("Customer Reference Number: 8596799759492")]
    public void Bpay_details_need_both(string? html) => Assert.Null(RenewalPages.ReadBpay(html));

    /* ───────────── Reading pages ───────────── */

    [Fact]
    public void Names_are_read_inside_adf_cdata_sections()
    {
        // ASIC's partial responses wrap the HTML in CDATA, which an HTML parser would drop.
        var partial = "<partial-response><changes><update id=\"tmpt:region\"><![CDATA[" + TwoNames + "]]></update></changes></partial-response>";

        Assert.Equal(["BEAUTIQUE HAIR LOUNGE & BEAUTY BAR", "SECOND NAME"], RenewalPages.Names(partial).Select(n => n.Name));
        Assert.Equal("tmpt:region:1:form:sbr1", RenewalPages.RadioFor(partial, "987654321"));
    }

    [Fact]
    public void A_partial_row_is_not_a_name() =>
        Assert.Equal(2, RenewalPages.Names(TwoNames + "<span class=\"dataText\">ORPHAN</span>").Count);

    [Theory]
    [InlineData(null, "0", "3")]
    [InlineData("<p>no ids</p>", "0", "3")]
    [InlineData("<span id=\"tmpt:region:5:payNow\"></span>", "5", "5")]
    [InlineData("<span id=\"tmpt:region:5:x\"></span><span id=\"tmpt:region:6:payNowPopup\"></span>", "5", "6")]
    public void Region_ids_come_from_the_current_page(string? page, string region, string payNowRegion)
    {
        Assert.Equal(region, RenewalPages.Region(page));
        Assert.Equal(payNowRegion, RenewalPages.PayNowRegion(page));
    }

    [Fact]
    public void Snippet_is_the_pages_text_cut_short() =>
        Assert.Equal(new string('a', 300) + "…", RenewalPages.Snippet("<p>" + new string('a', 400) + "</p>"));
}
