#nullable enable
using System.Net;
using System.Net.Security;
using System.Security.Authentication;


namespace Asic.Integrations;

/// <summary>The HTTP transport for the register search: own cookie jar, TLS 1.3 for Cloudflare, optional proxy. Ported from Businesstron.</summary>
public static class AsicRegistryHttp
{
    private static readonly string[] SupportedProxySchemes = ["http", "https", "socks4", "socks4a", "socks5"];

    /// <summary>A handler with its own cookie jar: the register's ADF session lives in cookies, so clients must not share one.</summary>
    public static SocketsHttpHandler CreateHandler(AsicRegistryOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        if (options.ForceTls13)
            handler.SslOptions = new SslClientAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls13 };

        // A bad address fails loudly rather than silently connecting directly.
        if (!TryCreateProxy(options, out var proxy, out var error))
        {
            handler.Dispose();
            throw new InvalidOperationException($"The ASIC register proxy is misconfigured. {error}");
        }
        if (proxy is not null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        return handler;
    }

    public static bool TryCreateProxy(AsicRegistryOptions options, out IWebProxy? proxy, out string? error)
    {
        proxy = null;
        error = null;
        var address = options.ProxyUrl?.Trim();
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

        var username = options.ProxyUsername?.Trim();
        NetworkCredential? credentials = null;
        if (!string.IsNullOrEmpty(username))
            credentials = new NetworkCredential(username, options.ProxyPassword ?? "");
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
}
