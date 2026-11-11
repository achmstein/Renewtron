using Asic.Integrations.Registry;

namespace Renewtron.Tests.AsicClients;

/// <summary>
/// Characterisation tests for <see cref="AsicRegistryClient"/> (ASIC's public register search,
/// where renewal due dates come from), pinned before the Asic.Integrations rewrite.
/// </summary>
public class AsicRegistryClientTests
{
    private const string Abn = "49642383462";

    // The register's loopback script spreads its arguments over lines.
    private const string Landing = "<script>AdfPage.x('_afrLoop',\n '55');\nAdfPage.y('_afrPage',\n '',\n 'rw0');</script>";
    private const string Hidden = """<input name="Adf-Window-Id" value="rw1"><input name="javax.faces.ViewState" value="!rvs">""";

    private static string Detail(int region, string name, string renewal) => $"""
        <div id="bnConnectionTemplate:r1:{region}:panel"></div>
        <table class="detailTable" id="bnConnectionTemplate:r1:{region}:dt1">
          <tr><th>Business name:</th><td>{name}</td></tr>
          <tr><th>Status:</th><td>Registered</td></tr>
          <tr><th>Registration date:</th><td>08/10/2020</td></tr>
          <tr><th>Renewal date:</th><td>{renewal}</td></tr>
          <tr><th>Address for service of documents:</th><td>1 Main St SYDNEY NSW 2000</td></tr>
          <tr><th>Holder(s) details:</th><td><span>Holder Name:</span><span>JANE CITIZEN</span><span>Holder Type:</span>Individual<div><span>ABN:</span></div><a>{Abn} (External Link)</a></td></tr>
        </table>
        """;

    private static readonly string Summary = Hidden + "<h2>Business Name Summary</h2>" + Detail(0, "BEAUTIQUE HAIR LOUNGE", "08/10/2027");

    private static string RichMessage(string html, string viewState) =>
        $"""<partial-response><changes><update id="javax.faces.ViewState"><![CDATA[{viewState}]]></update><update id="bnConnectionTemplate:r1"><![CDATA[{html}]]></update></changes></partial-response>""";

    private const string ResultsList = Hidden + """
        <h2>Business names search results</h2>
        <div id="bnConnectionTemplate:r1:0:list">
          <a id="bnConnectionTemplate:r1:0:t1:0:orgName">BEAUTIQUE HAIR LOUNGE</a>
          <a id="bnConnectionTemplate:r1:0:t1:1:orgName">SECOND NAME</a>
        </div>
        """;

    private sealed class FakeCaptcha(string? token) : ICaptchaSolver
    {
        public List<(string SiteKey, string Url)> Solves { get; } = [];
        public bool IsConfigured => token is not null;
        public Task<CaptchaSolveResult> SolveReCaptchaAsync(string siteKey, string pageUrl, string? action, bool invisible, bool enterprise, CancellationToken ct)
        {
            Solves.Add((siteKey, pageUrl));
            return Task.FromResult(CaptchaSolveResult.Ok(token!));
        }
    }

    private static (AsicRegistryClient Client, ScriptedHttp Http, FakeCaptcha Captcha) Client(string? captchaToken = null)
    {
        var http = new ScriptedHttp();
        var captcha = new FakeCaptcha(captchaToken);
        return (new AsicRegistryClient(new AsicRegistryOptions(), captcha, http), http, captcha);
    }

    [Fact]
    public async Task One_name_reads_its_summary_including_the_renewal_date()
    {
        var (client, http, captcha) = Client();
        http.Reply(Landing).Reply(Summary);

        var result = await client.SearchByAbnAsync(Abn, CancellationToken.None);

        Assert.True(result.Succeeded);
        var name = Assert.Single(result.BusinessNames);
        Assert.Equal(("BEAUTIQUE HAIR LOUNGE", "Registered", "08/10/2020", "08/10/2027"), (name.Name, name.Status, name.RegistrationDate, name.RenewalDate));
        Assert.Equal("1 Main St SYDNEY NSW 2000", name.AddressForServiceDocuments);
        var holder = Assert.Single(name.Holders);
        Assert.Equal(("JANE CITIZEN", "Individual", Abn), (holder.Name, holder.Type, holder.Abn));
        Assert.Empty(captcha.Solves);
        Golden.Verify("registry-summary", http.Transcript());
    }

    [Fact]
    public async Task Captcha_without_a_key_is_an_error_not_a_silent_miss()
    {
        var (client, http, _) = Client(captchaToken: null);
        http.Reply(Landing).Reply(Hidden + "<div class='g-recaptcha'></div>");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchByAbnAsync(Abn, CancellationToken.None));

        Assert.Equal("ASIC presented a CAPTCHA but no 2Captcha API key is configured. Set AsicRegistry:TwoCaptchaApiKey to read renewal dates.", ex.Message);
    }

    [Fact]
    public async Task Captcha_is_solved_and_the_search_carries_on()
    {
        var (client, http, captcha) = Client(captchaToken: "TOKEN-1");
        http.Reply(Landing).Reply(Hidden + "<div class='g-recaptcha'></div>").Reply(Summary);

        var result = await client.SearchByAbnAsync(Abn, CancellationToken.None);

        Assert.Equal("08/10/2027", Assert.Single(result.BusinessNames).RenewalDate);
        var solve = Assert.Single(captcha.Solves);
        Assert.Equal("6LdfxBoUAAAAAO7ItWGgMWT32_h5T_TtD4F1MflL", solve.SiteKey);
        Assert.Contains("g-recaptcha-response=TOKEN-1&", http.Requests[2].Body);
        Golden.Verify("registry-captcha", http.Transcript());
    }

    [Fact]
    public async Task Several_names_are_each_opened_in_turn()
    {
        var (client, http, _) = Client();
        http.Reply(Landing).Reply(ResultsList)
            .Reply(RichMessage(Detail(1, "BEAUTIQUE HAIR LOUNGE", "08/10/2027"), "!vs2"))
            .Reply(RichMessage(ResultsList, "!vs3"))
            .Reply(RichMessage(Detail(1, "SECOND NAME", "01/02/2028"), "!vs4"));

        var result = await client.SearchByAbnAsync(Abn, CancellationToken.None);

        Assert.True(result.HasMultiple);
        Assert.Equal([("BEAUTIQUE HAIR LOUNGE", "08/10/2027"), ("SECOND NAME", "01/02/2028")],
            result.BusinessNames.Select(n => (n.Name, n.RenewalDate)));
        Assert.Equal(0, http.Unused);
        Golden.Verify("registry-two-names", http.Transcript());
    }

    [Fact]
    public async Task Page_that_wont_load_is_a_failure()
    {
        var (client, http, _) = Client();
        http.Throw("network down");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchByAbnAsync(Abn, CancellationToken.None));
    }
}
