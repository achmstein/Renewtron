using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Services;

public class AirwallexPaymentService : IAirwallexPaymentService
{
    private readonly AirwallexSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AirwallexPaymentService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AirwallexPaymentService(
        IOptionsSnapshot<AirwallexSettings> settings,
        HttpClient httpClient,
        ILogger<AirwallexPaymentService> logger)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
    }

    public async Task<(bool Success, string? PaymentIntentId, string? ClientSecret, string? ErrorMessage)> CreatePaymentIntentAsync(
        decimal amount,
        string merchantOrderId,
        Dictionary<string, string> metadata)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var requestBody = new
            {
                amount,
                currency = "AUD",
                merchant_order_id = merchantOrderId,
                request_id = Guid.NewGuid().ToString(),
                metadata
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pa/payment_intents/create")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Airwallex create payment intent failed: {StatusCode} {Response}",
                    response.StatusCode, responseContent);
                return (false, null, null, $"Payment creation failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            var intentId = root.GetProperty("id").GetString();
            var clientSecret = root.GetProperty("client_secret").GetString();

            return (true, intentId, clientSecret, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Airwallex payment intent");
            return (false, null, null, ex.Message);
        }
    }

    public async Task<(bool Success, string? Status, string? ErrorMessage)> VerifyPaymentIntentAsync(string paymentIntentId)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pa/payment_intents/{paymentIntentId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Airwallex verify payment intent failed: {StatusCode} {Response}",
                    response.StatusCode, responseContent);
                return (false, null, $"Payment verification failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseContent);
            var status = doc.RootElement.GetProperty("status").GetString();

            return (status == "SUCCEEDED", status, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Airwallex payment intent");
            return (false, null, ex.Message);
        }
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/authentication/login");
        request.Headers.Add("x-client-id", _settings.ClientId);
        request.Headers.Add("x-api-key", _settings.ApiKey);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        return doc.RootElement.GetProperty("token").GetString()!;
    }
}
