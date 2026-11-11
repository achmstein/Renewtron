using System.Net;
using Asic.Integrations.Shared;

namespace Renewtron.Tests.AsicClients;

/// <summary>The plumbing every ASIC client shares: ADF's loopback script and the HTTP transport.</summary>
public class AsicSharedTests
{
    [Theory]
    [InlineData("x('_afrLoop', '4242'); y('_afrPage', '', 'w0');", "4242", "w0")]                                 // renewal, portal
    [InlineData("x('_afrLoop',\n '55');\ny('_afrPage',\n '',\n 'rw0');", "55", "rw0")]                            // register search
    [InlineData("y('Adf-Window-Id', '_afrPage', '', 'pw-1.2');x('_afrLoop', '77');", "77", "pw-1.2")]               // payment page
    public void Loopback_script_is_read_on_every_site(string script, string loop, string window)
    {
        Assert.Equal(loop, Adf.LoopId(script));
        Assert.Equal(window, Adf.ScriptWindowId(script));
    }

    [Fact]
    public void A_page_without_a_loopback_has_none()
    {
        Assert.Null(Adf.LoopId("<html></html>"));
        Assert.Null(Adf.ScriptWindowId(null));
    }

    [Fact]
    public void Loopback_query_matches_what_a_browser_sends() =>
        Assert.Equal(
            "_afrLoop=1&_afrWindowMode=0&Adf-Window-Id=w&_afrFS=16&_afrMT=screen&_afrMFW=1865&_afrMFH=924&_afrMFDW=1920&_afrMFDH=1080&_afrMFC=8&_afrMFCI=0&_afrMFM=0&_afrMFR=96&_afrMFG=0&_afrMFS=0&_afrMFO=0",
            Adf.LoopbackQuery("1", "w", windowMode: 0, windowHeight: 924));

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("http://gate.example:7000", true, "http://gate.example:7000/")]
    [InlineData("socks5://user:p%40ss@gate.example:1080", true, "socks5://gate.example:1080/")]
    [InlineData("ftp://gate.example", false, null)]
    [InlineData("not a url", false, null)]
    public void Proxy_address_is_checked(string? url, bool ok, string? address)
    {
        Assert.Equal(ok, AsicHttp.TryCreateProxy(new AsicHttpSettings { ProxyUrl = url }, out var proxy, out var error));
        Assert.Equal(ok, error is null);
        Assert.Equal(address, (proxy as WebProxy)?.Address?.ToString());
    }

    [Fact]
    public void Proxy_credentials_come_from_the_address_or_the_settings()
    {
        AsicHttp.TryCreateProxy(new AsicHttpSettings { ProxyUrl = "http://user:p%40ss@gate.example:7000" }, out var fromUrl, out _);
        Assert.Equal(("user", "p@ss"), Creds(fromUrl));

        AsicHttp.TryCreateProxy(new AsicHttpSettings { ProxyUrl = "http://u:x@gate.example:7000", ProxyUsername = "named", ProxyPassword = "secret" }, out var named, out _);
        Assert.Equal(("named", "secret"), Creds(named));

        static (string, string) Creds(IWebProxy? p) =>
            ((NetworkCredential)p!.Credentials!) is var c ? (c.UserName, c.Password) : default;
    }

    [Fact]
    public void Bad_proxy_fails_loudly_rather_than_connecting_directly() =>
        Assert.Throws<InvalidOperationException>(() => AsicHttp.CreateHandler(new AsicHttpSettings { ProxyUrl = "ftp://nope" }));

    [Fact]
    public void Expiring_cookies_starts_a_fresh_session()
    {
        var jar = new CookieContainer();
        var host = new Uri("https://asicconnect.asic.gov.au/");
        jar.Add(host, new Cookie("JSESSIONID", "abc"));

        AsicHttp.ExpireCookies(jar, host);

        Assert.Empty(jar.GetCookies(host));
    }
}
