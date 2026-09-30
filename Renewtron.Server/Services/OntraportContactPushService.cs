using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// Pushes people who come through the Renewtron wizard into Ontraport, so David's CRM sees
/// them the same way it sees Ontraport's own form fills. Every method runs as a Hangfire job
/// and throws on an Ontraport failure, so Hangfire's automatic retries cover outages.
///
/// The wizard is public and nothing proves the visitor owns the email they typed, so:
/// a contact id carried in on the URL is only used when that contact has the same email,
/// and an existing contact only has its empty fields filled — never overwritten.
/// </summary>
public interface IOntraportContactPushService
{
    /// <summary>
    /// Creates the lead's contact, or fills the gaps on the existing one: name, email, mobile,
    /// DOB, TFN, ABN, the business names found on the ABN (once checked) and the
    /// form-completed date. Adds the lead tag and stores the contact id on the lead.
    /// </summary>
    Task PushLeadAsync(Guid leadId);

    /// <summary>Records a wizard payment: the renewal term, and the paid tag.</summary>
    Task PushPaymentAsync(Guid leadId, int renewalYears);

    /// <summary>Records what ASIC did with a wizard renewal (status field, and the renewed tag on success).</summary>
    Task PushRenewalOutcomeAsync(Guid leadId, OntraportRenewalOutcome outcome);
}

public class OntraportContactPushService : IOntraportContactPushService
{
    // Same contact fields OntraportSalesService reads (see the ids there).
    private const string FieldBusinessName = "f5062";
    private const string FieldAbn = "f5063";
    private const string FieldMultipleBusinessNames = "f5215";
    private const string FieldRenewalTerm = "f5193"; // dropdown: 2033=1yr, 2034=3yr
    private const string FieldFormCompletedDate = "f5421"; // "date completed business renewal form"
    private const string FieldDateOfBirth = "DateOfBirt_233";
    private const string FieldTfn = "TaxFileNum_269";

    // Never written from here: f5194 ("Stripe Payment Recieved") = "yes" is what the daily
    // Ontraport sales sync treats as a new sale, so setting it would renew the name twice.

    private static readonly string[] ContactFields =
        ["id", "email", "firstname", "lastname", "sms_number", FieldDateOfBirth, FieldTfn, FieldAbn, FieldBusinessName, FieldMultipleBusinessNames];

    private readonly HttpClient _httpClient;
    private readonly ApplicationDbContext _db;
    private readonly IOntraportSalesService _ontraportSales;
    private readonly IOptionsSnapshot<OntraportSettings> _settings;
    private readonly ILogger<OntraportContactPushService> _logger;

    public OntraportContactPushService(
        HttpClient httpClient,
        ApplicationDbContext db,
        IOntraportSalesService ontraportSales,
        IOptionsSnapshot<OntraportSettings> settings,
        ILogger<OntraportContactPushService> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _ontraportSales = ontraportSales;
        _settings = settings;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.ontraport.com/1/");
        _httpClient.DefaultRequestHeaders.Add("Api-Appid", settings.Value.ApiAppId);
        _httpClient.DefaultRequestHeaders.Add("Api-Key", settings.Value.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private bool Enabled =>
        _settings.Value.PushWizardContacts
        && !string.IsNullOrWhiteSpace(_settings.Value.ApiAppId)
        && !string.IsNullOrWhiteSpace(_settings.Value.ApiKey);

    public async Task PushLeadAsync(Guid leadId)
    {
        if (!Enabled) return;

        var lead = await _db.Leads
            .Include(l => l.SearchLog).ThenInclude(s => s!.Results)
            .FirstOrDefaultAsync(l => l.Id == leadId);
        if (lead is null || string.IsNullOrWhiteSpace(lead.Email)) return;

        var (first, last) = SplitName(lead.FullName);
        var wanted = new Dictionary<string, object>
        {
            ["email"] = lead.Email.Trim(),
            ["firstname"] = first,
            ["lastname"] = last,
            [FieldAbn] = lead.Abn,
        };
        if (ToE164(lead.MobileNumber) is { } mobile) wanted["sms_number"] = mobile;
        if (lead.DateOfBirth != default)
            wanted[FieldDateOfBirth] = lead.DateOfBirth.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(lead.Tfn)) wanted[FieldTfn] = lead.Tfn;

        var names = lead.SearchLog?.Results.Select(r => r.BusinessName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
        if (names.Count > 0)
        {
            wanted[FieldBusinessName] = names[0];
            if (names.Count > 1) wanted[FieldMultipleBusinessNames] = string.Join(", ", names);
        }

        var existing = await FindContactAsync(lead);
        string contactId;
        if (existing is null)
        {
            wanted[FieldFormCompletedDate] = UnixSeconds(lead.CreatedAt);
            contactId = await CreateContactAsync(wanted);
        }
        else
        {
            contactId = existing["id"]!;
            // Fill gaps only: whoever typed this email may not own it.
            var fields = wanted
                .Where(f => f.Key != "email" && string.IsNullOrWhiteSpace(existing.GetValueOrDefault(f.Key)))
                .ToDictionary(f => f.Key, f => f.Value);
            // The form date just says someone filled the wizard in; always safe to move.
            fields[FieldFormCompletedDate] = UnixSeconds(lead.CreatedAt);
            fields["id"] = contactId;
            await UpdateContactAsync(fields);
        }

        if (lead.OntraportContactId != contactId)
        {
            lead.OntraportContactId = contactId;
            await _db.SaveChangesAsync();
        }

        await AddTagAsync(contactId, _settings.Value.WizardLeadTagId);
        _logger.LogInformation("Pushed lead {LeadId} to Ontraport contact {ContactId} ({Kind}, {NameCount} business names)",
            lead.Id, contactId, existing is null ? "new" : "existing", names.Count);
    }

    public async Task PushPaymentAsync(Guid leadId, int renewalYears)
    {
        var contactId = await EnsureContactAsync(leadId);
        if (contactId is null) return;

        var fields = new Dictionary<string, object> { ["id"] = contactId };
        if (renewalYears == 1) fields[FieldRenewalTerm] = "2033";
        else if (renewalYears == 3) fields[FieldRenewalTerm] = "2034";
        await UpdateContactAsync(fields);

        await AddTagAsync(contactId, _settings.Value.WizardPaidTagId);
        _logger.LogInformation("Pushed payment for lead {LeadId} to Ontraport contact {ContactId}", leadId, contactId);
    }

    public async Task PushRenewalOutcomeAsync(Guid leadId, OntraportRenewalOutcome outcome)
    {
        var contactId = await EnsureContactAsync(leadId);
        if (contactId is null) return;

        // No due date: we hold only the registration date for wizard names, not ASIC's expiry.
        if (!await _ontraportSales.SyncRenewalOutcomeAsync(contactId, outcome))
            throw new InvalidOperationException($"Ontraport rejected the renewal outcome for contact {contactId}; see earlier log lines.");
    }

    /// <summary>The lead's verified contact id, pushing the lead first if that never landed.</summary>
    private async Task<string?> EnsureContactAsync(Guid leadId)
    {
        if (!Enabled) return null;

        // Only an id PushLeadAsync stored can be trusted (it checked it); an id that came in
        // on the URL is re-checked by pushing the lead, which is idempotent.
        await PushLeadAsync(leadId);
        return await _db.Leads.Where(l => l.Id == leadId).Select(l => l.OntraportContactId).FirstOrDefaultAsync();
    }

    /// <summary>
    /// The lead's existing contact: the one whose id arrived on the URL if its email matches,
    /// otherwise the one with the lead's email. Null when there's none.
    /// </summary>
    private async Task<Dictionary<string, string?>?> FindContactAsync(Lead lead)
    {
        var email = lead.Email.Trim();
        if (!string.IsNullOrWhiteSpace(lead.OntraportContactId) && lead.OntraportContactId.All(char.IsDigit))
        {
            var byId = await GetContactsAsync($"Contacts?ids={lead.OntraportContactId}");
            var match = byId.FirstOrDefault(c => string.Equals(c.GetValueOrDefault("email")?.Trim(), email, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        var condition = Uri.EscapeDataString(JsonSerializer.Serialize(new[]
        {
            new { field = new { field = "email" }, op = "=", value = new { value = email } },
        }));
        var byEmail = await GetContactsAsync($"Contacts?condition={condition}&range=2");
        return byEmail.FirstOrDefault();
    }

    private async Task<List<Dictionary<string, string?>>> GetContactsAsync(string query)
    {
        var response = await _httpClient.GetAsync($"{query}&listFields={string.Join(",", ContactFields)}");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ontraport contact lookup returned HTTP {(int)response.StatusCode}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];
        return data.EnumerateArray()
            .Select(c => c.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString()))
            .ToList();
    }

    /// <summary>
    /// Creates the contact. Uses saveorupdate (merge on email) rather than a plain create, so
    /// the lead's two pushes racing each other seconds apart can't make two contacts.
    /// </summary>
    private async Task<string> CreateContactAsync(Dictionary<string, object> fields)
    {
        var response = await _httpClient.PostAsync("Contacts/saveorupdate", JsonBody(fields));
        var body = await response.Content.ReadAsStringAsync();
        // Field names only in errors: the values include the TFN and DOB.
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Ontraport contact create returned HTTP {(int)response.StatusCode} (fields: {string.Join(", ", fields.Keys)}): {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        // A new contact comes back as data.id, a merge as data.attrs.id.
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("id", out var id)) return id.ToString();
            if (data.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("id", out var attrId)) return attrId.ToString();
        }
        throw new InvalidOperationException($"Ontraport contact create returned no contact id: {Truncate(body)}");
    }

    private async Task UpdateContactAsync(Dictionary<string, object> fields)
    {
        if (fields.Count <= 1) return; // only the id — nothing to write

        var response = await _httpClient.PutAsync("Contacts", JsonBody(fields));
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Ontraport contact update returned HTTP {(int)response.StatusCode} (fields: {string.Join(", ", fields.Keys)}): {Truncate(body)}");
        }
    }

    private async Task AddTagAsync(string contactId, string? tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId)) return;

        var response = await _httpClient.PutAsync("objects/tag",
            JsonBody(new { objectID = 0, ids = new[] { contactId }, add_list = new[] { tagId.Trim() } }));
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Ontraport tag {tagId} on contact {contactId} returned HTTP {(int)response.StatusCode}: {Truncate(body)}");
        }
    }

    private static long UnixSeconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    private static string Truncate(string s) => s.Length > 500 ? s[..500] : s;

    private static (string First, string Last) SplitName(string fullName)
    {
        var parts = (fullName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (parts[0], ""),
            _ => (parts[0], parts[1]),
        };
    }

    /// <summary>Australian mobile as Ontraport stores it (+614xxxxxxxx); null when it doesn't look like one.</summary>
    internal static string? ToE164(string? mobile)
    {
        var digits = new string((mobile ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("61") && digits.Length == 11) return "+" + digits;
        if (digits.StartsWith("0") && digits.Length == 10) return "+61" + digits[1..];
        if (digits.StartsWith("4") && digits.Length == 9) return "+61" + digits;
        return null;
    }
}
