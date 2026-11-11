#nullable enable
using Asic.Client.Abstractions;
using Asic.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Asic.Client;

/// <summary>Solves reCAPTCHA through 2Captcha. Ported from Businesstron.</summary>
public sealed class TwoCaptchaSolver(IOptionsMonitor<AsicRegistryOptions> options, ILogger<TwoCaptchaSolver> logger) : ICaptchaSolver
{
    private const int DefaultTimeoutSeconds = 120;
    private const int RecaptchaTimeoutSeconds = 600;
    private const int PollingIntervalSeconds = 10;

    // CurrentValue so a key changed in settings is used on the next solve without a restart.
    private string? ApiKey => options.CurrentValue.TwoCaptchaApiKey;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<CaptchaSolveResult> SolveReCaptchaAsync(
        string siteKey, string pageUrl, string? action, bool invisible, bool enterprise, CancellationToken cancellationToken)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return CaptchaSolveResult.Fail("No 2Captcha API key is configured (AsicRegistry:TwoCaptchaApiKey).");

        try
        {
            var solver = new TwoCaptcha.TwoCaptcha(key)
            {
                DefaultTimeout = DefaultTimeoutSeconds,
                RecaptchaTimeout = RecaptchaTimeoutSeconds,
                PollingInterval = PollingIntervalSeconds,
            };
            var captcha = new TwoCaptcha.Captcha.ReCaptcha();
            captcha.SetSiteKey(siteKey);
            captcha.SetUrl(pageUrl);
            captcha.SetInvisible(invisible);
            captcha.SetEnterprise(enterprise);
            if (!string.IsNullOrEmpty(action)) captcha.SetAction(action);

            await solver.Solve(captcha);
            return string.IsNullOrEmpty(captcha.Code)
                ? CaptchaSolveResult.Fail("2Captcha returned an empty token.")
                : CaptchaSolveResult.Ok(captcha.Code);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "2Captcha solve failed for {PageUrl}", pageUrl);
            return CaptchaSolveResult.Fail(Describe(ex));
        }
    }

    private static string Describe(Exception ex)
    {
        var raw = ex.Message?.Trim();
        if (string.IsNullOrEmpty(raw)) return "2Captcha returned no result or timed out.";
        if (raw.Contains("ZERO_BALANCE", StringComparison.OrdinalIgnoreCase))
            return "2Captcha account balance is zero: top up the account to keep solving CAPTCHAs.";
        if (raw.Contains("WRONG_USER_KEY", StringComparison.OrdinalIgnoreCase) || raw.Contains("KEY_DOES_NOT_EXIST", StringComparison.OrdinalIgnoreCase))
            return "2Captcha rejected the API key (wrong or nonexistent).";
        if (raw.Contains("UNSOLVABLE", StringComparison.OrdinalIgnoreCase))
            return "2Captcha could not solve the CAPTCHA; it may succeed on a re-run.";
        if (raw.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            return "2Captcha timed out before returning a token.";
        return $"2Captcha error: {raw}";
    }
}

/// <summary>Creates register clients with the current options, so settings changes apply to the next lookup.</summary>
public sealed class AsicRegistryClientFactory(IOptionsMonitor<AsicRegistryOptions> options, ICaptchaSolver captcha) : IAsicRegistryClientFactory
{
    public IAsicRegistryClient Create() => new AsicRegistryClient(options.CurrentValue, captcha);
}
