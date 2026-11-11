namespace Asic.Integrations.Registry;

/// <summary>
/// Connection settings for ASIC's public business-names register search
/// (connectonline.asic.gov.au), bound from the "AsicRegistry" section.
/// </summary>
public class AsicRegistryOptions
{
    public const string SectionName = "AsicRegistry";

    public string BaseUrl { get; set; } = "https://connectonline.asic.gov.au/";

    /// <summary>ASIC's public reCAPTCHA site key (not a secret; it belongs to their page).</summary>
    public string RecaptchaSiteKey { get; set; } = "6LdfxBoUAAAAAO7ItWGgMWT32_h5T_TtD4F1MflL";

    /// <summary>2Captcha account key. The register search is behind reCAPTCHA; without a key, challenged lookups fail.</summary>
    public string? TwoCaptchaApiKey { get; set; }

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0";

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Advertise TLS 1.3 only. ASIC sits behind Cloudflare, which fingerprints the handshake and
    /// answers a default .NET ClientHello (1.2 + 1.3) with 403. Only turn off if ASIC drops TLS 1.3.
    /// </summary>
    public bool ForceTls13 { get; set; } = true;

    /// <summary>Outbound proxy, e.g. "http://host:port" or "socks5://host:port". Blank connects directly.</summary>
    public string? ProxyUrl { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }
}
