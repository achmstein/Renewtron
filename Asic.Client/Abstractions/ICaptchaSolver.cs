namespace Asic.Client.Abstractions;

/// <summary>
/// Outcome of a CAPTCHA solve: a token on success, or a human-readable reason on failure
/// (no key configured, provider error, timeout) so callers can surface <em>why</em>.
/// </summary>
public sealed class CaptchaSolveResult
{
    public string Token { get; init; }
    public string Error { get; init; }
    public bool Succeeded => !string.IsNullOrEmpty(Token);

    public static CaptchaSolveResult Ok(string token) => new() { Token = token };
    public static CaptchaSolveResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// Produces a g-recaptcha-response token for a page that runs reCAPTCHA v3. Abstracts the
/// solving provider (2Captcha in production) away from the ASIC clients so it can be
/// swapped or mocked.
/// </summary>
public interface ICaptchaSolver
{
    bool IsConfigured { get; }

    /// <param name="siteKey">The site key from the page's recaptcha/api.js?render=… script.</param>
    /// <param name="pageUrl">The URL of the page that executes the captcha.</param>
    /// <param name="action">The action name the page passes to grecaptcha.execute.</param>
    /// <param name="minScore">The score the target site requires (v3 is score-based; providers price by it).</param>
    Task<CaptchaSolveResult> SolveRecaptchaV3Async(string siteKey, string pageUrl, string action, double minScore, CancellationToken ct = default);

    /// <summary>Account balance at the provider, for a settings-page connection test. Throws on a bad key.</summary>
    Task<decimal> GetBalanceAsync(string apiKeyOverride = null, CancellationToken ct = default);
}
