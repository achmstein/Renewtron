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
        _retryPolicy = Policy<string>
            .Handle<OntraportException>()
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

    private async Task<List<ConversationMessage>> GetConversationMessagesAsync(string conversationId)
    {
        try
        {
            // Use the date parameter to filter messages from the start timestamp
            var url = $"Conversation/getMessages?id={conversationId}&date=&direction=0";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                throw new OntraportException($"Failed to fetch conversation messages: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OntraportConversationResponse>(content);

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

public class OntraportConversationResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public ConversationData? Data { get; set; }

    // Ontraport sends account_id as a JSON string ("12345"), not a number, so binding
    // it to `int` blew up the whole deserializer mid-conversation-fetch and failed
    // every renewal at "Process Payment". Match the rest of this file's ID fields
    // (UserId, ContactId, etc.) which are all string?.
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }
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