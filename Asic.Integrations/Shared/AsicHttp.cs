#nullable enable
using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace Asic.Integrations.Shared;

/// <summary>
/// How one client talks to ASIC over HTTP. Defaults match a plain <see cref="HttpClientHandler"/>:
/// each client sets only what it relies on, so moving a client onto <see cref="AsicHttp"/> does
/// not change what it sends.
/// </summary>
public sealed record AsicHttpSettings
{
    /// <summary>The client's own cookie jar (ADF sessions live in cookies, so clients never share one). Null: the handler makes its own.</summary>
    public CookieContainer? Cookies { get; init; }
    public bool AllowAutoRedirect { get; init; } = true;
    public DecompressionMethods Decompression { get; init; } = DecompressionMethods.None;

    /// <summary>
    /// Advertise TLS 1.3 only. Cloudflare in front of ASIC fingerprints the handshake and answers
    /// a default .NET ClientHello (1.2 + 1.3) with 403; a TLS-1.3-only hello looks like a browser.
    /// </summary>
    public bool ForceTls13 { get; init; }

    /// <summary>Recycle pooled connections after this long (picks up DNS changes). Null: never.</summary>
    public TimeSpan? PooledConnectionLifetime { get; init; }

    /// <summary>Outbound proxy, e.g. "http://host:port" or "socks5://user:pass@host:port". Blank connects directly.</summary>
    public string? ProxyUrl { get; init; }
    public string? ProxyUsername { get; init; }
    public string? ProxyPassword { get; init; }
}

/// <summary>Builds every ASIC client's HTTP transport, so TLS, proxy and cookie handling live in one place.</summary>
public static class AsicHttp
{
    private static readonly string[] SupportedProxySchemes = ["http", "https", "socks4", "socks4a", "socks5"];

    public static SocketsHttpHandler CreateHandler(AsicHttpSettings settings)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = settings.Cookies ?? new CookieContainer(),
            AllowAutoRedirect = settings.AllowAutoRedirect,
            AutomaticDecompression = settings.Decompression,
        };
        if (settings.PooledConnectionLifetime is { } lifetime) handler.PooledConnectionLifetime = lifetime;
        if (settings.ForceTls13)
            handler.SslOptions = new SslClientAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls13 };

        // A bad address fails loudly rather than silently connecting directly.
        if (!TryCreateProxy(settings, out var proxy, out var error))
        {
            handler.Dispose();
            throw new InvalidOperationException($"The ASIC proxy is misconfigured. {error}");
        }
        if (proxy is not null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        return handler;
    }

    /// <summary>An <see cref="HttpClient"/> over <paramref name="handler"/> that disposes it, with the client's base address, timeout and browser user agent.</summary>
    public static HttpClient CreateClient(HttpMessageHandler handler, string? baseAddress = null, TimeSpan? timeout = null, string? userAgent = null)
    {
        var http = new HttpClient(handler, disposeHandler: true);
        if (baseAddress is not null) http.BaseAddress = new Uri(baseAddress);
        if (timeout is { } t) http.Timeout = t;
        if (userAgent is not null) http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return http;
    }

    public static bool TryCreateProxy(AsicHttpSettings settings, out IWebProxy? proxy, out string? error)
    {
        proxy = null;
        error = null;
        var address = settings.ProxyUrl?.Trim();
        if (string.IsNullOrEmpty(address)) return true;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            error = "The proxy address must be an absolute URL, for example http://host:port.";
            return false;
        }
        if (!SupportedProxySchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            error = $"Proxy scheme '{uri.Scheme}' is not supported. Use one of: {string.Join(", ", SupportedProxySchemes)}.";
            return false;
        }

        var username = settings.ProxyUsername?.Trim();
        NetworkCredential? credentials = null;
        if (!string.IsNullOrEmpty(username))
            credentials = new NetworkCredential(username, settings.ProxyPassword ?? "");
        else if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }

        var bare = string.IsNullOrEmpty(uri.UserInfo) ? uri : new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        var web = new WebProxy(bare) { BypassProxyOnLocal = false };
        if (credentials is not null) web.Credentials = credentials;
        proxy = web;
        return true;
    }

    /// <summary>Expires every cookie the jar holds for <paramref name="hosts"/>: a fresh ADF session next time.</summary>
    public static void ExpireCookies(CookieContainer cookies, params Uri[] hosts)
    {
        foreach (var host in hosts)
            foreach (Cookie cookie in cookies.GetCookies(host))
            {
                cookie.Expired = true;
                cookie.Expires = DateTime.Now.AddDays(-1);
            }
    }
}
