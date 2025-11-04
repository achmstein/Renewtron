using Asic.Client.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Renewtron.Settings;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Renewtron.Services;

public class OntraportSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly OntraportSettings _settings;
    private readonly ILogger<OntraportSmsProvider> _logger;
    private readonly AsyncRetryPolicy<string> _retryPolicy;

    public OntraportSmsProvider(
        HttpClient httpClient,
        IOptionsSnapshot<OntraportSettings> settings,
        ILogger<OntraportSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        // Configure default headers
        _httpClient.BaseAddress = new Uri("https://api.ontraport.com/1/");
        _httpClient.DefaultRequestHeaders.Add("Api-Appid", _settings.ApiAppId);
        _httpClient.DefaultRequestHeaders.Add("Api-Key", _settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Configure retry policy for polling SMS
        _retryPolicy = Policy<string>
            .Handle<OntraportException>()
            .OrResult(sms => string.IsNullOrWhiteSpace(sms))
            .WaitAndRetryAsync(
                retryCount: 20, // Poll up to 20 times
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(3), // Wait 3 seconds between polls
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    var message = outcome.Exception?.Message ?? "SMS not yet available";
                    _logger.LogInformation($"[Polling SMS - Attempt {retryAttempt}/20] {message}. Retrying in {timespan.TotalSeconds} seconds...");
                });
    }

    public async Task<bool> InitializeAsync()
    {
        var cleared = await ClearLastInboundSmsAsync(_settings.ContactId);

        if (cleared)
        {
            _logger.LogInformation($"Successfully cleared last_inbound_sms for contact {_settings.ContactId}");
        }
        else
        {
            _logger.LogWarning($"Warning: Failed to clear last_inbound_sms for contact {_settings.ContactId}");
        }

        return cleared;
    }

    public async Task<string> GetOtpAsync()
    {
        try
        {
            // Use Polly retry policy to poll for SMS with OTP
            var smsText = await _retryPolicy.ExecuteAsync(async () =>
            {
                var sms = await GetLastInboundSmsAsync(_settings.ContactId);

                // Check if SMS contains OTP pattern
                if (!string.IsNullOrWhiteSpace(sms) && ContainsOtpPattern(sms))
                {
                    return sms;
                }

                // Return empty to trigger retry
                return string.Empty;
            });

            // Extract the OTP code
            var otpCode = ExtractOtpCode(smsText);

            if (string.IsNullOrWhiteSpace(otpCode))
            {
                throw new OntraportException($"Could not extract OTP code from SMS: {smsText}");
            }

            _logger.LogInformation($"Successfully retrieved OTP: {otpCode}");

            // Clear the SMS field to prevent conflicts with future payments
            var cleared = await ClearLastInboundSmsAsync(_settings.ContactId);
            if (cleared)
            {
                _logger.LogInformation($"Successfully cleared last_inbound_sms for contact {_settings.ContactId}");
            }
            else
            {
                _logger.LogWarning($"Warning: Failed to clear last_inbound_sms for contact {_settings.ContactId}");
            }

            return otpCode;
        }
        catch (Exception ex) when (ex is not OntraportException)
        {
            throw new OntraportException($"Error waiting for OTP: {ex.Message}", ex);
        }
    }

    private async Task<string> GetLastInboundSmsAsync(string contactId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"Contact?id={contactId}");

            if (!response.IsSuccessStatusCode)
            {
                throw new OntraportException($"Failed to fetch contact: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OntraportResponse>(content);

            if (result?.Code != 0 || result?.Data == null)
            {
                throw new OntraportException("Invalid response from Ontraport API");
            }

            // Extract last_inbound_sms from the data object
            if (result.Data.TryGetProperty("last_inbound_sms", out var smsElement))
            {
                return smsElement.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex) when (ex is not OntraportException)
        {
            throw new OntraportException($"Error retrieving SMS from Ontraport: {ex.Message}", ex);
        }
    }

    private async Task<bool> ClearLastInboundSmsAsync(string contactId)
    {
        try
        {
            var formData = new Dictionary<string, string>
            {
                ["id"] = contactId,
                ["last_inbound_sms"] = string.Empty
            };

            var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PutAsync("Contacts", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to clear SMS field: {response.StatusCode}");
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OntraportResponse>(responseContent);

            return result?.Code == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error clearing SMS field: {ex.Message}");
            return false;
        }
    }

    private bool ContainsOtpPattern(string smsText)
    {
        if (string.IsNullOrWhiteSpace(smsText))
            return false;

        // Check for common OTP patterns
        var patterns = new[]
        {
            @"code\s+(?:is|required|:)\s+\d{4,8}",
            @"\b\d{6}\b", // 6-digit codes
            @"\b\d{4}\b", // 4-digit codes
            @"\b\d{8}\b"  // 8-digit codes
        };

        return patterns.Any(pattern =>
            Regex.IsMatch(smsText, pattern, RegexOptions.IgnoreCase));
    }

    private string ExtractOtpCode(string smsText)
    {
        if (string.IsNullOrWhiteSpace(smsText))
            return string.Empty;

        // Pattern 1: "code is XXXXXX" or "code required...is XXXXXX"
        var pattern1 = Regex.Match(
            smsText,
            @"code\s+(?:is|required[^:]*is|:)\s+(\d{4,8})",
            RegexOptions.IgnoreCase);

        if (pattern1.Success)
        {
            return pattern1.Groups[1].Value;
        }

        // Pattern 2: Look for standalone 6-8 digit numbers (most common OTP length)
        var pattern2 = Regex.Match(smsText, @"\b(\d{6,8})\b");
        if (pattern2.Success)
        {
            return pattern2.Groups[1].Value;
        }

        // Pattern 3: Look for 4-digit codes (less common but possible)
        var pattern3 = Regex.Match(smsText, @"\b(\d{4})\b");
        if (pattern3.Success)
        {
            return pattern3.Groups[1].Value;
        }

        return string.Empty;
    }
}

public class OntraportException : Exception
{
    public OntraportException(string message) : base(message) { }
    public OntraportException(string message, Exception innerException)
        : base(message, innerException) { }
}

public class OntraportResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }
}