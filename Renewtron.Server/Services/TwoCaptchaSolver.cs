using System.Globalization;
using System.Text.Json;
using Asic.Client.Abstractions;
using Microsoft.Extensions.Options;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// reCAPTCHA v3 tokens from 2Captcha's classic HTTP API (in.php / res.php). Kept to plain
/// HttpClient rather than the 2captcha-csharp package: two endpoints, no surprises. The
/// API key is read per call so a Settings edit applies without a restart.
/// </summary>
public sealed class TwoCaptchaSolver : ICaptchaSolver
{
    private const string BaseUrl = "https://2captcha.com/";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SolveTimeout = TimeSpan.FromSeconds(150);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<AsicKeyRequestSettings> _settings;
    private readonly ILogger<TwoCaptchaSolver> _logger;

    public TwoCaptchaSolver(HttpClient http, IOptionsMonitor<AsicKeyRequestSettings> settings, ILogger<TwoCaptchaSolver> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri(BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.CurrentValue.TwoCaptchaApiKey);

    public async Task<CaptchaSolveResult> SolveRecaptchaV3Async(string siteKey, string pageUrl, string action, double minScore, CancellationToken ct = default)
    {
        var apiKey = _settings.CurrentValue.TwoCaptchaApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return CaptchaSolveResult.Fail("No 2Captcha API key is configured (Settings → ASIC key requests).");

        try
        {
            var form = new Dictionary<string, string>
            {
                ["key"] = apiKey,
                ["method"] = "userrecaptcha",
                ["version"] = "v3",
                ["googlekey"] = siteKey,
                ["pageurl"] = pageUrl,
                ["action"] = action,
                ["min_score"] = minScore.ToString("0.0#", CultureInfo.InvariantCulture),
                ["json"] = "1",
            };
            using var submit = await _http.PostAsync("in.php", new FormUrlEncodedContent(form), ct);
            var (submitOk, taskId) = Parse(await submit.Content.ReadAsStringAsync(ct));
            if (!submitOk)
                return CaptchaSolveResult.Fail(Describe(taskId));

            var deadline = DateTime.UtcNow + SolveTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, ct);
                using var poll = await _http.GetAsync($"res.php?key={Uri.EscapeDataString(apiKey)}&action=get&id={Uri.EscapeDataString(taskId)}&json=1", ct);
                var (ok, value) = Parse(await poll.Content.ReadAsStringAsync(ct));
                if (ok)
                {
                    _logger.LogInformation("2Captcha solved reCAPTCHA v3 (min score {MinScore}) for {PageUrl}", minScore, pageUrl);
                    return CaptchaSolveResult.Ok(value);
                }
                if (value == "CAPCHA_NOT_READY")
                    continue;
                return CaptchaSolveResult.Fail(Describe(value));
            }
            return CaptchaSolveResult.Fail("2Captcha timed out before returning a token.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "2Captcha solve failed for {PageUrl}", pageUrl);
            return CaptchaSolveResult.Fail($"2Captcha request failed: {ex.Message}");
        }
    }

    public async Task<decimal> GetBalanceAsync(string? apiKeyOverride = null, CancellationToken ct = default)
    {
        var apiKey = (string.IsNullOrWhiteSpace(apiKeyOverride) ? _settings.CurrentValue.TwoCaptchaApiKey : apiKeyOverride)?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No 2Captcha API key is configured.");

        using var response = await _http.GetAsync($"res.php?key={Uri.EscapeDataString(apiKey)}&action=getbalance&json=1", ct);
        var (ok, value) = Parse(await response.Content.ReadAsStringAsync(ct));
        if (!ok)
            throw new InvalidOperationException(Describe(value));
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    // {"status":1,"request":"<id or token or balance>"} / {"status":0,"request":"ERROR_…"}
    private static (bool Ok, string Value) Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
            var request = doc.RootElement.TryGetProperty("request", out var r) ? r.ToString() : "";
            return (status == 1, request);
        }
        catch (JsonException)
        {
            return (false, json.Length > 200 ? json[..200] : json);
        }
    }

    private static string Describe(string code) => code switch
    {
        "ERROR_ZERO_BALANCE" => "2Captcha account balance is zero — top up the account to keep solving captchas.",
        "ERROR_WRONG_USER_KEY" or "ERROR_KEY_DOES_NOT_EXIST" => "2Captcha rejected the API key (wrong or nonexistent). Check it in Settings → ASIC key requests.",
        "ERROR_CAPTCHA_UNSOLVABLE" => "2Captcha could not solve the captcha (ERROR_CAPTCHA_UNSOLVABLE); a retry may succeed.",
        "ERROR_NO_SLOT_AVAILABLE" => "2Captcha has no workers free right now; a retry may succeed.",
        "ERROR_IP_BLOCKED" or "ERROR_IP_NOT_ALLOWED" => "2Captcha blocked this server's IP — check the account's IP restrictions.",
        _ => $"2Captcha error: {code}",
    };
}
