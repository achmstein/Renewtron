using System.Net;
using AngleSharp.Html.Parser;
using Asic.Client;
using Asic.Client.Abstractions;
using Asic.Client.Models;

namespace Renewtron.Tests.AsicClients;

/// <summary>
/// Characterisation tests for <see cref="AsicPaymentClient"/> (ASIC's payment page and the
/// Worldline card gateway behind it), pinned before the Asic.Integrations rewrite.
/// </summary>
public class AsicPaymentClientTests
{
    private const string PaymentUrl = "https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx?SessionId=S1&SST=T1";

    private const string PaymentLanding = "<script>AdfPage.runLoopback(1);</script><script>x('_afrLoop', '77'); y('Adf-Window-Id', '_afrPage', '', 'pw1');</script>";
    private const string PaymentLoopback = """<input name="javax.faces.ViewState" value="!pvs"><span title="hiddenpaymenturl"> https://payment.anzworldline-solutions.com.au/hostedtokenization/form/TOK1 </span>""";

    private static readonly CreditCardDetails Card = new()
    {
        CardNumber = "4111111111111111", CardholderName = "Test Holder", ExpiryMonth = "12", ExpiryYear = "2030", Cvc = "123",
    };

    private sealed class FakeChallenge(string host, bool completes) : IThreeDSChallengeHandler
    {
        public int Calls;
        public bool CanHandle(Uri challengeUrl) => challengeUrl.Host == host;
        public Task<StepResult<PaymentStepData>> HandleChallengeAsync(HttpClient httpClient, HtmlParser htmlParser, PaymentStepData data)
        {
            Calls++;
            if (completes) data.ThreeDSRequiresOtp = false;
            return Task.FromResult(StepResult<PaymentStepData>.Success(data));
        }
    }

    private static ScriptedHttp UpToDeviceInfo(ScriptedHttp http) => http
        .Reply(PaymentLanding)
        .Reply(PaymentLoopback)
        .Reply("<html>tokenization form</html>")
        .Reply("""{"brands":[{"name":"visa"}]}""")
        .Reply("""{"threeDSServerTransactionId":"3ds-1","methodUrl":"https://acs.example/method","version":"2.2.0"}""")
        .Reply("""{"hostedTokenizationId":"HT-9"}""");

    private const string ChallengeRedirect = "<partial-response><eval>window.location.href = 'https://payment.anzworldline-solutions.com.au/v1/redirect/handlerequest/g1'</eval></partial-response>";
    private const string ChallengeForm = """<form id="redirect" action="https://acs.example-bank.com/challenge?a=1&amp;b=2"><input name="creq" value="CREQ"><input name="threeDSSessionData" value="SD"></form>""";

    [Fact]
    public async Task Frictionless_payment_tokenises_the_card_and_closes_the_window()
    {
        var http = UpToDeviceInfo(new ScriptedHttp())
            .Reply("""<partial-response><redirect url="https://asicconnect.asic.gov.au/public/paymentSuccess.jsp?x=1&amp;y=2"/></partial-response>""")
            .Reply("");
        var client = new AsicPaymentClient([], http);

        var result = await client.ProcessPaymentAsync(PaymentUrl, "7E1234567-AB", Card);

        Assert.True(result.Success, result.Message);
        Assert.Equal("HT-9", result.HostedTokenizationId);
        Assert.Equal(0, http.Unused);
        Golden.Verify("payment-frictionless", http.Transcript());
    }

    [Fact]
    public async Task Issuer_decline_without_a_challenge_is_reported()
    {
        var http = UpToDeviceInfo(new ScriptedHttp())
            .Reply("""<redirect url="https://asicconnect.asic.gov.au/public/paymentFailure.jsp"/>""");
        var client = new AsicPaymentClient([], http);

        var result = await client.ProcessPaymentAsync(PaymentUrl, "7E1234567-AB", Card);

        Assert.False(result.Success);
        Assert.Equal("Payment was declined by the issuer", result.Message);
        Assert.Equal(7, http.Requests.Count); // no window close on failure
    }

    [Fact]
    public async Task Challenge_goes_to_the_handler_for_the_issuers_host()
    {
        var http = UpToDeviceInfo(new ScriptedHttp()).Reply(ChallengeRedirect).Reply(ChallengeForm).Reply("<html>challenge</html>").Reply("");
        var handler = new FakeChallenge("acs.example-bank.com", completes: true);
        var client = new AsicPaymentClient([new FakeChallenge("other.example", true), handler], http);

        var result = await client.ProcessPaymentAsync(PaymentUrl, "7E1234567-AB", Card);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, http.Unused);
        Golden.Verify("payment-challenge", http.Transcript());
    }

    [Fact]
    public async Task Challenge_left_unfinished_is_a_failure()
    {
        var http = UpToDeviceInfo(new ScriptedHttp()).Reply(ChallengeRedirect).Reply(ChallengeForm).Reply("<html>challenge</html>").Reply("");
        var client = new AsicPaymentClient([new FakeChallenge("acs.example-bank.com", completes: false)], http);

        var result = await client.ProcessPaymentAsync(PaymentUrl, "7E1234567-AB", Card);

        Assert.False(result.Success);
        Assert.Equal("3DS Challenge requires OTP but was not completed. Transaction ID: , Issuer ID: , Challenge URL: https://acs.example-bank.com/challenge?a=1&b=2", result.Message);
    }

    [Fact]
    public async Task Challenge_from_an_unknown_issuer_is_refused()
    {
        var http = UpToDeviceInfo(new ScriptedHttp()).Reply(ChallengeRedirect).Reply(ChallengeForm).Reply("<html>challenge</html>");
        var client = new AsicPaymentClient([], http);

        var result = await client.ProcessPaymentAsync(PaymentUrl, "7E1234567-AB", Card);

        Assert.Equal("No 3DS challenge handler registered for host 'acs.example-bank.com'", result.Message);
    }

    [Fact]
    public async Task Payment_url_without_session_and_token_sends_nothing()
    {
        var http = new ScriptedHttp();
        var result = await new AsicPaymentClient([], http).ProcessPaymentAsync("https://regpayment.asic.gov.au/AsicPayment/faces/index.jspx", "x", Card);

        Assert.Equal("Missing required parameters in payment URL", result.Message);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("<p>no script</p>", "ADF loopback script not found")]
    [InlineData("<script>AdfPage.runLoopback(1);</script>", "Missing ADF parameters in script")]
    public async Task Unreadable_payment_page_stops_at_once(string page, string message)
    {
        var http = new ScriptedHttp().Reply(page);
        var result = await new AsicPaymentClient([], http).ProcessPaymentAsync(PaymentUrl, "x", Card);

        Assert.Equal(message, result.Message);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Card_brand_not_recognised_stops_before_the_card_is_sent()
    {
        var http = new ScriptedHttp().Reply(PaymentLanding).Reply(PaymentLoopback).Reply("form").Reply("""{"brands":[]}""");
        var result = await new AsicPaymentClient([], http).ProcessPaymentAsync(PaymentUrl, "x", Card);

        Assert.Equal("No card brand detected", result.Message);
        Assert.DoesNotContain(http.Requests, r => r.Body.Contains("cvc="));
    }

    [Fact]
    public async Task Tokenisation_http_error_names_the_status()
    {
        var http = new ScriptedHttp().Reply(PaymentLanding).Reply(PaymentLoopback).Reply("form")
            .Reply("""{"brands":[{"name":"visa"}]}""").Reply("{}").Reply("bad", HttpStatusCode.BadRequest);
        var result = await new AsicPaymentClient([], http).ProcessPaymentAsync(PaymentUrl, "x", Card);

        Assert.Equal("HTTP BadRequest", result.Message);
    }
}
