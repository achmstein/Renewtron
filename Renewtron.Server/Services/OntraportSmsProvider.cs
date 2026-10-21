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

        // SMS-forwarding to Ontraport has been observed to take up to ~9 minutes end-to-end, so the
        // total window must comfortably exceed that. 60 polls × 10s = 10 minutes.
        // A conversation that no longer exists won't come back by waiting, so that one fails at once.
        _retryPolicy = Policy<string>
            .Handle<OntraportException>(ex => ex is not OtpConversationMissingException)
            .OrResult(sms => string.IsNullOrWhiteSpace(sms))
            .WaitAndRetryAsync(
                retryCount: 60,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(10),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    var message = outcome.Exception?.Message ?? "SMS not yet available";
                    _logger.LogInformation($"[Polling SMS - Attempt {retryAttempt}/60] {message}. Retrying in {timespan.TotalSeconds} seconds...");
                });
    }

    public async Task<string> GetOtpAsync()
    {
        try
        {
            // Capture the current timestamp - only process messages AFTER this time
            var startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _logger.LogInformation($"Starting OTP polling for conversation {_settings.ConversationId} from timestamp {startTimestamp}");

            // Use Polly retry policy to poll for new SMS messages with OTP
            var smsText = await _retryPolicy.ExecuteAsync(async () =>
            {
                var messages = await GetConversationMessagesAsync(_settings.ConversationId);

                // Look for messages received AFTER we started waiting
                var newMessages = messages
                    .Where(m =>
                        long.TryParse(m.Date, out var msgDate) &&
                        msgDate > startTimestamp &&
                        m.Type == "CONVO_MESSAGE" &&
                        m.Topic == "TEXT" &&
                        !string.IsNullOrWhiteSpace(m.Resource))
                    .OrderBy(m => m.Date) // Process oldest first
                    .ToList();

                // Check if any new message contains OTP
                foreach (var message in newMessages)
                {
                    if (ContainsOtpPattern(message.Resource!))
                    {
                        _logger.LogInformation($"Found OTP in message ID {message.Id} dated {message.Date}");
                        return message.Resource!;
                    }
                }

                // No OTP found yet, trigger retry
                return string.Empty;
            });

            if (string.IsNullOrWhiteSpace(smsText))
            {
                throw new OntraportException(
                    "No OTP SMS received within the 10-minute polling window. " +
                    "The issuer may have declined the transaction (e.g. card overlimit/blocked) " +
                    "or SMS forwarding to Ontraport is delayed beyond the window.");
            }

            // Extract the OTP code
            var otpCode = ExtractOtpCode(smsText);

            if (string.IsNullOrWhiteSpace(otpCode))
            {
                throw new OntraportException($"Could not extract OTP code from SMS: {smsText}");
            }

            _logger.LogInformation($"Successfully retrieved OTP: {otpCode}");

            return otpCode;
        }
        catch (Exception ex) when (ex is not OntraportException)
        {
            throw new OntraportException($"Error waiting for OTP: {ex.Message}", ex);
        }
    }

    public async Task<string?> CheckAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ConversationId))
            return "No Ontraport OTP conversation is set. Set ConversationId on the Settings page.";
        try
        {
            await GetConversationMessagesAsync(_settings.ConversationId);
            return null;
        }
        catch (OntraportException ex)
        {
            return ex.Message;
        }
    }

    // Ontraport sends the same field as a string one day and a number the next (account_id
    // has gone both ways, each time failing every renewal), so read any JSON value into the
    // text fields and accept numbers written as strings.
    private static readonly JsonSerializerOptions LenientJson = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new AnyValueAsStringConverter(), new LenientBoolConverter() },
    };

    private async Task<List<ConversationMessage>> GetConversationMessagesAsync(string conversationId)
    {
        try
        {
            // Use the date parameter to filter messages from the start timestamp
            var url = $"Conversation/getMessages?id={conversationId}&date=&direction=0";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Ontraport answers 400 "Object not found." once the conversation is deleted or
                // recreated (it has happened to 915, 925, 957 and 965). Say so plainly.
                if (content.Contains("Object not found", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OtpConversationMissingException(
                        $"Ontraport OTP conversation {conversationId} no longer exists. " +
                        "Find the conversation that receives ASIC's SMS codes and update ConversationId on the Settings page.");
                }
                throw new OntraportException($"Failed to fetch conversation messages: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<OntraportConversationResponse>(content, LenientJson);

            if (result?.Code != 0 || result?.Data == null)
            {
                throw new OntraportException("Invalid response from Ontraport Conversation API");
            }

            return result.Data.Messages ?? new List<ConversationMessage>();
        }
        catch (Exception ex) when (ex is not OntraportException)
        {
            throw new OntraportException($"Error retrieving conversation messages from Ontraport: {ex.Message}", ex);
        }
    }

    private bool ContainsOtpPattern(string smsText)
    {
        if (string.IsNullOrWhiteSpace(smsText))
            return false;

        // Check for common OTP patterns
        var patterns = new[]
        {
            @"code\b.*?\bis\s+\d{4,8}",  // "code...is XXXXXX"
            @"code\s*(?:is|:)\s*\d{4,8}", // "code is XXXXXX" or "code: XXXXXX"
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

        // Pattern 1: "code...is XXXXXX" (handles "secret code to complete...is 209679")
        var pattern1 = Regex.Match(
            smsText,
            @"code\b.*?\bis\s+(\d{4,8})",
            RegexOptions.IgnoreCase);

        if (pattern1.Success)
        {
            return pattern1.Groups[1].Value;
        }

        // Pattern 2: "code is XXXXXX" or "code: XXXXXX"
        var pattern2 = Regex.Match(
            smsText,
            @"code\s*(?:is|:)\s*(\d{4,8})",
            RegexOptions.IgnoreCase);

        if (pattern2.Success)
        {
            return pattern2.Groups[1].Value;
        }

        // Pattern 3: Look for standalone 6-8 digit numbers (most common OTP length)
        var pattern3 = Regex.Match(smsText, @"\b(\d{6,8})\b");
        if (pattern3.Success)
        {
            return pattern3.Groups[1].Value;
        }

        // Pattern 4: Look for 4-digit codes (less common but possible)
        var pattern4 = Regex.Match(smsText, @"\b(\d{4})\b");
        if (pattern4.Success)
        {
            return pattern4.Groups[1].Value;
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

/// <summary>The configured OTP conversation is gone from Ontraport; retrying can't help.</summary>
public class OtpConversationMissingException : OntraportException
{
    public OtpConversationMissingException(string message) : base(message) { }
}

public class OntraportConversationResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public ConversationData? Data { get; set; }

    // Ontraport has sent account_id as a string and as a number; LenientJson reads either.
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }
}

/// <summary>Reads any JSON value into a string: strings as they are, numbers and booleans as
/// their text, objects and arrays as raw JSON.</summary>
public sealed class AnyValueAsStringConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Number => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

/// <summary>Booleans as true/false, 1/0 or their string forms.</summary>
public sealed class LenientBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetInt64(out var n) && n != 0,
            JsonTokenType.String => reader.GetString()?.Trim().ToLowerInvariant() is "true" or "1",
            _ => false,
        };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

public class ConversationData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("max_date")]
    public string? MaxDate { get; set; }

    [JsonPropertyName("hasNext")]
    public bool HasNext { get; set; }

    [JsonPropertyName("hasPrev")]
    public bool HasPrev { get; set; }

    [JsonPropertyName("messages")]
    public List<ConversationMessage> Messages { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class ConversationMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("contact_id")]
    public string? ContactId { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("merge_data")]
    public string? MergeData { get; set; }

    [JsonPropertyName("vtype")]
    public string? VType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("attachments")]
    public string? Attachments { get; set; }

    [JsonPropertyName("cc")]
    public string? Cc { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("object_type_id")]
    public string? ObjectTypeId { get; set; }

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("sender_data")]
    public string? SenderData { get; set; }

    [JsonPropertyName("thread_meta")]
    public string? ThreadMeta { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }
}