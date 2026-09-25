using System.Text.Json;

namespace Asic.KeyTool;

/// <summary>A paid renewal from Ontraport — one business name that needs its ASIC key.</summary>
public sealed class Sale
{
    public string ContactId { get; init; } = "";
    public string ContactName { get; init; } = "";
    /// <summary>The client's own email — ASIC's form asks where to reply, and that's them.</summary>
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string BusinessName { get; init; } = "";
    public string Abn { get; init; } = "";
    public decimal AmountPaid { get; init; }
    public DateTime? RenewalDueDate { get; init; }

    /// <summary>Why this sale shouldn't be sent to ASIC (cancelled, refunded, disputed), or null.</summary>
    public string? IneligibleReason { get; init; }

    public Enquiry ToEnquiry()
    {
        var enquiry = new Enquiry
        {
            ContactId = ContactId,
            BusinessName = BusinessName,
            Abn = Abn,
            Email = Email,
            Phone = Phone,
            RenewalDueDate = RenewalDueDate,
        };
        enquiry.SetContactName(ContactName);
        return enquiry;
    }
}

/// <summary>
/// Reads new paid renewals straight from Ontraport, the same way Renewtron's sales sync
/// does: contacts whose "Stripe Payment Recieved" field (f5194) is "yes", newest activity
/// first. Field IDs are Ontraport's and must match the ones the server uses.
/// </summary>
public sealed class OntraportClient
{
    private const string BaseUrl = "https://api.ontraport.com/1/";

    // Ontraport custom field IDs for business name renewal data.
    private const string FieldBusinessName = "f5062";
    private const string FieldAbn = "f5063";
    private const string FieldRenewalDueDate = "f5135";
    private const string FieldPaymentReceived = "f5194"; // text: "yes" = paid, "dispute" = refund requested
    private const string FieldCancel = "f5418";          // set = customer is cancelling, not renewing

    private readonly KeyToolSettings _settings;

    public OntraportClient(KeyToolSettings settings) => _settings = settings;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.OntraportApiAppId) &&
        !string.IsNullOrWhiteSpace(_settings.OntraportApiKey);

    /// <summary>
    /// Every paid contact that has a business name and ABN, newest first. Ineligible ones
    /// come back too, carrying their reason, so the console can show what it's holding back
    /// rather than silently dropping rows.
    /// </summary>
    public async Task<List<Sale>> FetchPaidSalesAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("No Ontraport API credentials (menu → Settings).");

        // Condition format: the value must be wrapped in {"value":"…"} for Ontraport's API.
        var condition = Uri.EscapeDataString(
            "[{\"field\":{\"field\":\"" + FieldPaymentReceived + "\"},\"op\":\"=\",\"value\":{\"value\":\"yes\"}}]");
        var fields = string.Join(',',
            "id", "firstname", "lastname", "email", "sms_number", "spent", "refund",
            FieldBusinessName, FieldAbn, FieldRenewalDueDate, FieldPaymentReceived, FieldCancel);

        // The most recent 1000 paid contacts in one call; anything already requested is
        // filtered out locally, so there's no need to page back further.
        var url = $"Contacts?condition={condition}&range={Math.Clamp(_settings.OntraportFetchLimit, 1, 1000)}" +
                  $"&listFields={fields}&sortDir=desc&sort=dla";

        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Api-Appid", _settings.OntraportApiAppId.Trim());
        request.Headers.TryAddWithoutValidation("Api-Key", _settings.OntraportApiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? "Ontraport rejected the credentials (check the App ID and API key in Settings)."
                : $"Ontraport returned {(int)response.StatusCode} {response.ReasonPhrase}.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var sales = new List<Sale>();
        foreach (var element in data.EnumerateArray())
        {
            var contact = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                contact[property.Name] = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString();

            var businessName = Value(contact, FieldBusinessName);
            var abn = Value(contact, FieldAbn);
            if (businessName.Length == 0 || abn.Length == 0) continue;

            var amountPaid = decimal.TryParse(Value(contact, "spent"), out var spent) ? spent : 0m;
            sales.Add(new Sale
            {
                ContactId = Value(contact, "id"),
                ContactName = $"{Value(contact, "firstname")} {Value(contact, "lastname")}".Trim(),
                Email = Value(contact, "email"),
                Phone = Value(contact, "sms_number"),
                BusinessName = businessName,
                Abn = abn,
                AmountPaid = amountPaid,
                RenewalDueDate = UnixDate(Value(contact, FieldRenewalDueDate)),
                IneligibleReason = IneligibilityReason(contact, amountPaid, _settings.MinimumAmountPaid),
            });
        }
        return sales;
    }

    /// <summary>
    /// The same guards Renewtron applies before renewing: a cancellation, a refund or a
    /// payment field that no longer reads "yes" means don't spend a captcha on this one.
    /// </summary>
    private static string? IneligibilityReason(Dictionary<string, string?> contact, decimal amountPaid, decimal minimumAmountPaid)
    {
        var paymentReceived = Value(contact, FieldPaymentReceived);
        if (!string.Equals(paymentReceived, "yes", StringComparison.OrdinalIgnoreCase))
            return $"payment field reads '{paymentReceived}'";

        var cancel = Value(contact, FieldCancel);
        if (cancel.Length > 0 && cancel != "0")
            return "cancel field is set";

        if (decimal.TryParse(Value(contact, "refund"), out var refunds) && refunds > 0)
            return $"{refunds:0.##} refund(s) on file";

        if (minimumAmountPaid > 0 && amountPaid < minimumAmountPaid)
            return $"paid {amountPaid:0.00}, below the {minimumAmountPaid:0.00} minimum";

        return null;
    }

    private static string Value(Dictionary<string, string?> contact, string key) =>
        (contact.GetValueOrDefault(key) ?? "").Trim();

    private static DateTime? UnixDate(string value) =>
        long.TryParse(value, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}
