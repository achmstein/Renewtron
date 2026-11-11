using System.Net;
using AngleSharp.Html.Parser;
using Asic.Integrations.Payment;
using Asic.Integrations.Payment.ThreeDS;
using Asic.Integrations.Shared;

namespace Renewtron.Tests.AsicClients;

/// <summary>
/// Characterisation tests for the 3DS challenge handlers (RSA, Cardinal, Arcot): the issuer's
/// pages are scripted, the OTP comes from a fake SMS inbox.
/// </summary>
public class ThreeDSChallengeHandlerTests
{
    private sealed class FakeSms(string? otp) : ISmsProvider
    {
        public int Reads;
        public Task<string> GetOtpAsync() { Reads++; return otp is null ? throw new TimeoutException("no SMS arrived") : Task.FromResult(otp); }
        public Task<string?> CheckAsync() => Task.FromResult<string?>(null);
    }

    private static PaymentStepData Data(string challengeUrl, string? challengePage = null)
    {
        var data = PaymentStepData.Create("https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SessionId=S1&SST=T1", "7E1234567-AB",
            new CreditCardDetails { CardNumber = "4111111111111111", CardholderName = "Test Holder", ExpiryMonth = "12", ExpiryYear = "2030", Cvc = "123" });
        data.BrandName = "visa";
        data.ThreeDSChallengeUrl = challengeUrl;
        data.ThreeDSChallengeResponseContent = challengePage;
        data.ThreeDSRequiresOtp = true;
        return data;
    }

    private static async Task<(StepResult<PaymentStepData> Result, ScriptedHttp Http)> Run(IThreeDSChallengeHandler handler, PaymentStepData data, ScriptedHttp http)
    {
        var result = await handler.HandleChallengeAsync(new HttpClient(http), new HtmlParser(), data);
        return (result, http);
    }

    [Theory]
    [InlineData("https://nab.rsa3dsauth.co.uk/acs/creq", "rsa")]
    [InlineData("https://authentication.cardinalcommerce.com/ThreeDSecure/V2_1_0/CReq", "cardinal")]
    [InlineData("https://secure7.arcot.com/acspage/cap", "arcot")]
    public void Each_handler_takes_its_issuers_host(string url, string which)
    {
        var sms = new FakeSms("1");
        IThreeDSChallengeHandler[] all = [new RsaChallengeHandler(sms), new CardinalChallengeHandler(sms), new ArcotChallengeHandler(sms)];
        var taken = all.Where(h => h.CanHandle(new Uri(url))).Select(h => h.GetType().Name.Replace("ChallengeHandler", "").ToLowerInvariant());
        Assert.Equal([which], taken);
    }

    /* ───────────── RSA ───────────── */

    private const string RsaUrl = "https://nab.rsa3dsauth.co.uk/acs/creq";
    private const string RsaChoose = """<form id="mainForm" action="/acs/challengeSubmit?x=1&amp;y=2"><input name="challengeWindowSize" value="05"><input name="threeDSServerTransID" value="srv-1"><input name="messageVersion" value="2.2.0"><input name="acsTransID" value="acs-1"><input type="radio" name="dataEntry" value="001"></form>""";
    private const string RsaOtpForm = """<form id="mainForm" action="/acs/challengeSubmit"><input name="acsTransID" value="acs-2"><input type="password" name="dataEntry"></form>""";
    private const string RsaCres = """<form id="cres_submit" action="https://payment.anzworldline-solutions.com.au/v1/redirect/handleresponse"><input name="cres" value="CRES"><input name="threeDSSessionData" value="SD"></form>""";

    [Fact]
    public async Task Rsa_picks_the_sms_credential_enters_the_otp_and_returns_the_cres()
    {
        var sms = new FakeSms("654321");
        var (result, http) = await Run(new RsaChallengeHandler(sms), Data(RsaUrl, RsaChoose),
            new ScriptedHttp().Reply(RsaOtpForm).Reply(RsaCres).Reply("ok"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Value.ThreeDSComplete);
        Assert.False(result.Value.ThreeDSRequiresOtp);
        Assert.Equal(("acs-2", "srv-1"), (result.Value.ThreeDSAcsTransId, result.Value.ThreeDSTransactionId));
        Assert.Equal(1, sms.Reads);
        Golden.Verify("3ds-rsa", http.Transcript());
    }

    [Fact]
    public async Task Rsa_without_a_credential_choice_goes_straight_to_the_otp()
    {
        var page = RsaChoose.Replace("""<input type="radio" name="dataEntry" value="001">""", """<input type="password" name="dataEntry">""");
        var (result, http) = await Run(new RsaChallengeHandler(new FakeSms("1")), Data(RsaUrl, page), new ScriptedHttp().Reply(RsaCres).Reply("", HttpStatusCode.Found));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Rsa_otp_form_shown_again_means_the_code_was_wrong()
    {
        var (result, _) = await Run(new RsaChallengeHandler(new FakeSms("000000")), Data(RsaUrl, RsaChoose),
            new ScriptedHttp().Reply(RsaOtpForm).Reply(RsaOtpForm));

        Assert.Equal(("Invalid OTP code", "Submit OTP"), (result.ErrorMessage, result.StepName));
    }

    [Fact]
    public async Task Rsa_otp_that_never_arrives_stops_before_submitting()
    {
        var (result, http) = await Run(new RsaChallengeHandler(new FakeSms(null)), Data(RsaUrl, RsaChoose), new ScriptedHttp().Reply(RsaOtpForm));

        Assert.Equal("Failed to retrieve OTP: no SMS arrived", result.ErrorMessage);
        Assert.Single(http.Requests);
    }

    [Theory]
    [InlineData("<p>no form</p>", "Could not find RSA challenge form")]
    [InlineData("""<form action="/x"><input name="acsTransID" value="a"></form>""", "RSA form missing required fields")]
    public async Task Rsa_unreadable_challenge_page(string page, string message)
    {
        var (result, http) = await Run(new RsaChallengeHandler(new FakeSms("1")), Data(RsaUrl, page), new ScriptedHttp());

        Assert.Equal(message, result.ErrorMessage);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Rsa_cres_rejected_by_the_gateway()
    {
        var (result, _) = await Run(new RsaChallengeHandler(new FakeSms("1")), Data(RsaUrl, RsaChoose),
            new ScriptedHttp().Reply(RsaOtpForm).Reply(RsaCres).Reply("no", HttpStatusCode.BadRequest));

        Assert.Equal("cres submission failed: BadRequest", result.ErrorMessage);
    }

    /* ───────────── Cardinal ───────────── */

    private const string CardinalUrl = "https://authentication.cardinalcommerce.com/ThreeDSecure/V2_1_0/CReq?oid=O1&tid=T1";

    [Fact]
    public async Task Cardinal_chooses_sms_reads_the_stepup_id_and_validates_the_otp()
    {
        var (result, http) = await Run(new CardinalChallengeHandler(new FakeSms("112233")), Data(CardinalUrl),
            new ScriptedHttp().Reply("""{"NextStep":"VALIDATE"}""").Reply("""<script>var model = { StepupRequestId: "SR-1" };</script>""").Reply("""{"NextStep":"COMPLETE"}"""));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Value.ThreeDSComplete);
        Assert.False(result.Value.ThreeDSRequiresOtp);
        Assert.Equal(("O1", "T1", "SR-1"), (result.Value.CardinalOid, result.Value.CardinalTid, result.Value.CardinalStepupRequestId));
        Golden.Verify("3ds-cardinal", http.Transcript());
    }

    [Fact]
    public async Task Cardinal_reads_the_stepup_id_from_a_hidden_input_too()
    {
        var (result, _) = await Run(new CardinalChallengeHandler(new FakeSms("1")), Data(CardinalUrl),
            new ScriptedHttp().Reply("""{"NextStep":"VALIDATE"}""").Reply("""<input type="hidden" name="StepupRequestId" value="SR-2">""").Reply("""{"NextStep":"RESULT"}"""));

        Assert.Equal("SR-2", result.Value.CardinalStepupRequestId);
    }

    [Fact]
    public async Task Cardinal_asking_to_re_enter_means_the_code_was_wrong()
    {
        var (result, _) = await Run(new CardinalChallengeHandler(new FakeSms("1")), Data(CardinalUrl),
            new ScriptedHttp().Reply("""{"NextStep":"VALIDATE"}""").Reply("page").Reply("""{"NextStep":"VALIDATE","Message":{"Content":"Please re-enter your code"}}"""));

        Assert.Equal("Invalid OTP code", result.ErrorMessage);
    }

    [Fact]
    public async Task Cardinal_other_next_step_reports_success_but_leaves_the_otp_unfinished()
    {
        // Today: the handler succeeds, and the payment client then reports the OTP as not completed.
        var (result, _) = await Run(new CardinalChallengeHandler(new FakeSms("1")), Data(CardinalUrl),
            new ScriptedHttp().Reply("""{"NextStep":"VALIDATE"}""").Reply("page").Reply("""{"NextStep":"CHOICE"}"""));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ThreeDSRequiresOtp);
        Assert.False(result.Value.ThreeDSComplete);
    }

    [Fact]
    public async Task Cardinal_without_sms_option_stops()
    {
        var (result, http) = await Run(new CardinalChallengeHandler(new FakeSms("1")), Data(CardinalUrl), new ScriptedHttp().Reply("""{"NextStep":"CHOICE"}"""));

        Assert.Equal("Unexpected NextStep: CHOICE", result.ErrorMessage);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Cardinal_url_without_ids_sends_nothing()
    {
        var (result, http) = await Run(new CardinalChallengeHandler(new FakeSms("1")), Data("https://authentication.cardinalcommerce.com/x"), new ScriptedHttp());

        Assert.Equal("Cardinal challenge URL missing oid/tid", result.ErrorMessage);
        Assert.Empty(http.Requests);
    }

    /* ───────────── Arcot ───────────── */

    private const string ArcotUrl = "https://secure7.arcot.com/acspage/cap?x=1";
    private const string ArcotChoose = """<form action="/v1/challenge"><input name="acs_account_id" value="ACC1"><input type="radio" name="selected_options" value="email1"><input type="radio" name="selected_options" value="mobilenumber2"></form>""";
    private const string ArcotVerify = """<form id="verify_form" action="/v1/challenge/verify"><input name="text_input"><input name="acs_account_id" value="ACC2"><input name="bb_user_transaction_id" value="BB1"></form>""";
    private const string ArcotCres = """<form id="cres_form" action="https://payment.anzworldline-solutions.com.au/v1/redirect/handleresponse"><input name="cres" value="CRES2"></form>""";

    [Fact]
    public async Task Arcot_picks_the_mobile_option_enters_the_otp_and_returns_the_cres()
    {
        var (result, http) = await Run(new ArcotChallengeHandler(new FakeSms("998877")), Data(ArcotUrl, ArcotChoose),
            new ScriptedHttp().Reply(ArcotVerify).Reply(ArcotCres).Reply("ok"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Value.ThreeDSComplete);
        Assert.Equal("ACC1", result.Value.ThreeDSIssuerId);
        Assert.Contains("selected_options=mobilenumber2", http.Requests[0].Body);
        Golden.Verify("3ds-arcot", http.Transcript());
    }

    [Fact]
    public async Task Arcot_without_a_mobile_option_asks_for_mobilenumber1()
    {
        var page = ArcotChoose.Replace("mobilenumber2", "email2");
        var (_, http) = await Run(new ArcotChallengeHandler(new FakeSms("1")), Data(ArcotUrl, page),
            new ScriptedHttp().Reply(ArcotVerify).Reply(ArcotCres).Reply("ok"));

        Assert.Contains("selected_options=mobilenumber1", http.Requests[0].Body);
    }

    [Fact]
    public async Task Arcot_verify_form_shown_again_means_the_code_was_wrong()
    {
        var (result, _) = await Run(new ArcotChallengeHandler(new FakeSms("1")), Data(ArcotUrl, ArcotChoose),
            new ScriptedHttp().Reply(ArcotVerify).Reply(ArcotVerify));

        Assert.Equal("Invalid OTP code", result.ErrorMessage);
    }

    [Theory]
    [InlineData("<p>none</p>", "Could not find Arcot challenge form")]
    [InlineData("""<form action="/v1/challenge"></form>""", "Arcot form missing acs_account_id")]
    public async Task Arcot_unreadable_challenge_page(string page, string message)
    {
        var (result, http) = await Run(new ArcotChallengeHandler(new FakeSms("1")), Data(ArcotUrl, page), new ScriptedHttp());

        Assert.Equal(message, result.ErrorMessage);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Arcot_sms_trigger_failure_is_reported()
    {
        var (result, _) = await Run(new ArcotChallengeHandler(new FakeSms("1")), Data(ArcotUrl, ArcotChoose),
            new ScriptedHttp().Reply("err", HttpStatusCode.ServiceUnavailable));

        Assert.Equal("Arcot SMS trigger failed: ServiceUnavailable", result.ErrorMessage);
    }
}
