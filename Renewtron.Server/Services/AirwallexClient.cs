using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Services;

public class AirwallexClient : IAirwallexClient
{
    /// <summary>ASIC as the payee. BPAY routes on the biller code; the name is for Airwallex's records.</summary>
    private const string AsicName = "Australian Securities and Investments Commission";

    // Tokens last 30 minutes and Airwallex asks not to log in per request. Keyed by
    // environment + client id, so switching keys or sandbox/live in Settings logs in afresh.
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> Tokens = new();
    private static readonly SemaphoreSlim LoginLock = new(1, 1);

    private readonly HttpClient _http;
    private readonly AirwallexSettings _settings;
    private readonly ILogger<AirwallexClient> _logger;

    public AirwallexClient(HttpClient http, IOptionsSnapshot<AirwallexSettings> settings, ILogger<AirwallexClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<AirwallexResult> CreateBpayTransferAsync(string requestId, string billerCode, string customerReference,
        decimal amount, string reference, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["request_id"] = requestId,
            ["source_currency"] = "AUD",
            ["transfer_currency"] = "AUD",
            ["transfer_amount"] = amount,
            ["transfer_method"] = "LOCAL",
            ["reason"] = "bill_payment",
            ["reference"] = reference,
            ["beneficiary"] = new JsonObject
            {
                ["type"] = "BANK_ACCOUNT",
                ["entity_type"] = "COMPANY",
                ["company_name"] = AsicName,
                ["bank_details"] = new JsonObject
                {
                    ["account_currency"] = "AUD",
                    ["account_name"] = AsicName,
                    ["bank_country_code"] = "AU",
                    ["local_clearing_system"] = "BPAY",
                    ["account_routing_type1"] = "bpay_biller_code",
                    ["account_routing_value1"] = Digits(billerCode),
                    ["account_routing_type2"] = "bpay_customer_reference",
                    ["account_routing_value2"] = Digits(customerReference),
                },
            },
        };
        return SendAsync(HttpMethod.Post, "/api/v1/transfers/create", body, ct);
    }

    public Task<AirwallexResult> GetTransferAsync(string transferId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/api/v1/transfers/{Uri.EscapeDataString(transferId)}", null, ct);

    public async Task<AirwallexResult> FindTransferByRequestIdAsync(string requestId, CancellationToken ct = default)
    {
        var result = await SendAsync(HttpMethod.Get, $"/api/v1/transfers?request_id={Uri.EscapeDataString(requestId)}", null, ct,
            json => json["items"] is JsonArray { Count: > 0 } items ? items[0] : null);
        return result;
    }

    public async Task<string?> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!_settings.Ready) return "Airwallex client ID and API key aren't set.";
        try
        {
            Tokens.TryRemove(CacheKey, out _);
            await TokenAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string CacheKey => $"{_settings.BaseUrl}|{_settings.ClientId}";

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (Tokens.TryGetValue(CacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.Token;

        await LoginLock.WaitAsync(ct);
        try
        {
            if (Tokens.TryGetValue(CacheKey, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return cached.Token;

            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.BaseUrl + "/api/v1/authentication/login");
            request.Headers.Add("x-client-id", _settings.ClientId);
            request.Headers.Add("x-api-key", _settings.ApiKey);
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Airwallex login failed ({(int)response.StatusCode}): {ErrorMessage(text)}");

            var json = JsonNode.Parse(text)!;
            var token = json["token"]!.GetValue<string>();
            var expiresAt = DateTimeOffset.TryParse(json["expires_at"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var e) ? e : DateTimeOffset.UtcNow.AddMinutes(25);
            Tokens[CacheKey] = (token, expiresAt);
            return token;
        }
        finally
        {
            LoginLock.Release();
        }
    }

    private async Task<AirwallexResult> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct,
        Func<JsonNode, JsonNode?>? pick = null)
    {
        if (!_settings.Ready)
            return new AirwallexResult(null, "Airwallex isn't set up (Settings → Airwallex).");

        string token;
        try
        {
            token = await TokenAsync(ct);
        }
        catch (Exception ex)
        {
            // Nothing was sent yet, so this is certain.
            return new AirwallexResult(null, ex.Message);
        }

        try
        {
            using var request = new HttpRequestMessage(method, _settings.BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                Tokens.TryRemove(CacheKey, out _);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Airwallex {Method} {Path} -> {Status}: {Body}", method, path, (int)response.StatusCode, text);
                // A 5xx may have been processed anyway; a 4xx was refused.
                return new AirwallexResult(null, $"Airwallex said ({(int)response.StatusCode}): {ErrorMessage(text)}",
                    Uncertain: (int)response.StatusCode >= 500);
            }

            var json = JsonNode.Parse(text)!;
            var node = pick is null ? json : pick(json);
            return new AirwallexResult(node is null ? null : ToTransfer(node), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Airwallex {Method} {Path} got no answer", method, path);
            return new AirwallexResult(null, $"No answer from Airwallex: {ex.Message}", Uncertain: true);
        }
    }

    private static AirwallexTransfer ToTransfer(JsonNode json) => new(
        json["id"]?.GetValue<string>() ?? "",
        json["status"]?.GetValue<string>() ?? "UNKNOWN",
        json["failure_reason"]?.GetValue<string>() ?? json["failure_type"]?.GetValue<string>());

    /// <summary>Airwallex errors are { code, message, source }; fall back to the raw body.</summary>
    private static string ErrorMessage(string body)
    {
        try
        {
            var json = JsonNode.Parse(body);
            var message = json?["message"]?.GetValue<string>();
            var source = json?["source"]?.GetValue<string>();
            var code = json?["code"]?.GetValue<string>();
            if (message is not null)
                return string.Join(" ", new[] { code is null ? null : $"[{code}]", message, source is null ? null : $"({source})" }
                    .Where(p => p is not null));
        }
        catch (JsonException)
        {
        }
        // Not JSON — e.g. an HTML error page from in front of the API. Keep its text only.
        var text = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " "), @"\s+", " ").Trim();
        if (text.Length == 0) text = "(empty response)";
        return text.Length > 300 ? text[..300] : text;
    }

    /// <summary>BPAY codes are digits only; ASIC's page may show spaces.</summary>
    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());
}
