using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asic.Client.Abstractions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

public class OntraportSalesService : IOntraportSalesService
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IAsicRenewalClient _asicClient;
    private readonly IOptionsSnapshot<PricingSettings> _pricingSettings;
    private readonly ILogger<OntraportSalesService> _logger;

    // Ontraport custom field IDs for business name renewal data
    private const string FieldBusinessName = "f5062";
    private const string FieldAbn = "f5063";
    private const string FieldBusinessNameOwner = "f5064";
    private const string FieldRenewalDueDate = "f5135";
    private const string FieldRenewalTerm = "f5193"; // dropdown: 2033=1yr, 2034=3yr, 2090=5yr
    private const string FieldPaymentReceived = "f5194"; // text: "yes" = paid, "dispute" = refund requested
    private const string FieldCancel = "f5418"; // dropdown: 2185 = Business Name Only, 2186 = ABN and Business Name (customer cancelled)
    private const string FieldRenewalStatus = "f5481"; // what we did last time we tried to renew — see OntraportRenewalOutcome
    private const string FieldDateOfBirth = "DateOfBirt_233";

    // "checked business name due" tag, added once a renewal goes through
    private const string RenewalCompletedTagId = "1810";

    // Renewal term dropdown value mapping
    private static readonly Dictionary<string, int> RenewalTermMap = new()
    {
        { "2033", 1 },
        { "2034", 3 },
        { "2090", 5 }
    };

    public OntraportSalesService(
        HttpClient httpClient,
        ApplicationDbContext dbContext,
        IBackgroundJobClient backgroundJobClient,
        IAsicRenewalClient asicClient,
        IOptionsSnapshot<OntraportSettings> settings,
        IOptionsSnapshot<PricingSettings> pricingSettings,
        ILogger<OntraportSalesService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _backgroundJobClient = backgroundJobClient;
        _asicClient = asicClient;
        _pricingSettings = pricingSettings;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.ontraport.com/1/");
        _httpClient.DefaultRequestHeaders.Add("Api-Appid", settings.Value.ApiAppId);
        _httpClient.DefaultRequestHeaders.Add("Api-Key", settings.Value.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<OntraportSale>> SyncSalesAsync()
    {
        _logger.LogInformation("Starting Ontraport sales sync");
        var synced = new List<OntraportSale>();

        try
        {
            // Single query: get all contacts where payment received = "yes"
            // Then filter for business name in code (Ontraport != condition is unreliable)
            var contacts = await FetchPaidContactsAsync();
            _logger.LogInformation("Fetched {Count} paid contacts from Ontraport", contacts.Count);

            // Get already-synced contact IDs to skip duplicates
            var syncedContactIds = await _dbContext.OntraportSales
                .Select(s => s.OntraportContactId)
                .ToHashSetAsync();

            foreach (var contact in contacts)
            {
                try
                {
                    var contactId = contact.GetValueOrDefault("id", "");
                    if (string.IsNullOrEmpty(contactId) || syncedContactIds.Contains(contactId))
                        continue;

                    // Skip contacts without business name data
                    var businessName = contact.GetValueOrDefault(FieldBusinessName, "");
                    var abn = contact.GetValueOrDefault(FieldAbn, "");
                    if (string.IsNullOrWhiteSpace(businessName) || string.IsNullOrWhiteSpace(abn))
                        continue;

                    // Parse renewal due date
                    DateTime? renewalDueDate = null;
                    var dueDateStr = contact.GetValueOrDefault(FieldRenewalDueDate, "0");
                    if (long.TryParse(dueDateStr, out var dueDateUnix) && dueDateUnix > 0)
                        renewalDueDate = DateTimeOffset.FromUnixTimeSeconds(dueDateUnix).UtcDateTime;

                    // Parse renewal term
                    var termValue = contact.GetValueOrDefault(FieldRenewalTerm, "0");
                    var renewalYears = RenewalTermMap.GetValueOrDefault(termValue, 1);

                    // Amount from spent field
                    var spentStr = contact.GetValueOrDefault("spent", "0");
                    var amountPaid = decimal.TryParse(spentStr, out var spent) ? spent : 0;

                    // Determine status: eligible when strictly under 30 days (i.e. <= 29)
                    var status = OntraportSaleStatus.Synced;
                    if (renewalDueDate.HasValue)
                    {
                        var daysUntilDue = (renewalDueDate.Value - DateTime.UtcNow).TotalDays;
                        status = daysUntilDue <= 29
                            ? OntraportSaleStatus.Synced
                            : OntraportSaleStatus.WaitingForRenewalWindow;
                    }

                    // Skip cancellations, refunds, disputes, and underpayments so they never auto-renew
                    var ineligibleReason = GetIneligibilityReason(contact, amountPaid);
                    string? errorMessage = null;
                    if (ineligibleReason != null)
                    {
                        status = OntraportSaleStatus.IneligibleForRenewal;
                        errorMessage = ineligibleReason;
                    }

                    var sale = new OntraportSale
                    {
                        Id = Guid.NewGuid(),
                        OntraportContactId = contactId,
                        OntraportTransactionId = "",
                        ContactName = $"{contact.GetValueOrDefault("firstname", "")} {contact.GetValueOrDefault("lastname", "")}".Trim(),
                        Email = contact.GetValueOrDefault("email", ""),
                        MobileNumber = contact.GetValueOrDefault("sms_number", ""),
                        DateOfBirth = contact.GetValueOrDefault(FieldDateOfBirth, null),
                        BusinessName = businessName,
                        Abn = abn,
                        BusinessNameOwner = contact.GetValueOrDefault(FieldBusinessNameOwner, null),
                        RenewalDueDate = renewalDueDate,
                        RenewalYears = renewalYears,
                        AmountPaid = amountPaid,
                        Status = status,
                        ErrorMessage = errorMessage,
                        SyncedAt = DateTime.UtcNow
                    };

                    _dbContext.OntraportSales.Add(sale);
                    synced.Add(sale);

                    _logger.LogInformation("Synced Ontraport sale: {BusinessName} (ABN: {Abn}), due: {DueDate}, contact: {ContactName}",
                        businessName, abn, renewalDueDate?.ToString("yyyy-MM-dd") ?? "unknown", sale.ContactName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing contact {ContactId}", contact.GetValueOrDefault("id", "?"));
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Synced {Count} new Ontraport sales", synced.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Ontraport sales sync");
        }

        return synced;
    }

    public async Task ProcessEligibleRenewalsAsync()
    {
        _logger.LogInformation("Processing eligible Ontraport renewals");

        // Find sales that are within 30 days of due date and haven't been processed yet
        var now = DateTime.UtcNow;
        var eligibleSales = await _dbContext.OntraportSales
            .Where(s => s.Status == OntraportSaleStatus.Synced
                     || s.Status == OntraportSaleStatus.WaitingForRenewalWindow
                     || s.Status == OntraportSaleStatus.AsicNotYetDue
                     || s.Status == OntraportSaleStatus.RenewalInProgress)
            .Where(s => s.RenewalDueDate != null)
            .Where(s => s.RenewalRequestId == null)
            .ToListAsync();

        foreach (var sale in eligibleSales)
        {
            var daysUntilDue = (sale.RenewalDueDate!.Value - now).TotalDays;

            if (daysUntilDue > 29)
            {
                // Not yet in the renewal window (David wants strictly under 30 days)
                if (sale.Status != OntraportSaleStatus.WaitingForRenewalWindow)
                {
                    sale.Status = OntraportSaleStatus.WaitingForRenewalWindow;
                }
                continue;
            }

            if (daysUntilDue < -30)
            {
                // Way past due, skip
                sale.Status = OntraportSaleStatus.NotDueForRenewal;
                sale.ErrorMessage = "Renewal due date is more than 30 days in the past";
                continue;
            }

            try
            {
                // Re-verify with Ontraport before queueing: catches disputes/refunds/cancellations
                // that were applied between the 06:00 sync and the 07:00 processing run.
                var liveContact = await FetchContactByIdAsync(sale.OntraportContactId);
                if (liveContact != null)
                {
                    var liveSpentStr = liveContact.GetValueOrDefault("spent", "0");
                    var liveAmountPaid = decimal.TryParse(liveSpentStr, out var liveSpent) ? liveSpent : sale.AmountPaid;
                    var ineligibleReason = GetIneligibilityReason(liveContact, liveAmountPaid);
                    if (ineligibleReason != null)
                    {
                        _logger.LogWarning("Skipping renewal for Ontraport contact {ContactId} ({BusinessName}): {Reason}",
                            sale.OntraportContactId, sale.BusinessName, ineligibleReason);
                        sale.Status = OntraportSaleStatus.IneligibleForRenewal;
                        sale.ErrorMessage = ineligibleReason;
                        sale.AmountPaid = liveAmountPaid;
                        continue;
                    }
                    sale.AmountPaid = liveAmountPaid;
                }

                _logger.LogInformation("Queuing renewal for {BusinessName} (ABN: {Abn}), due in {Days} days",
                    sale.BusinessName, sale.Abn, (int)daysUntilDue);

                // Resolve the real ASIC account number (not the ABN) by searching ASIC and matching the business name.
                // The renewal flow selects a row by account number, so storing the ABN here causes "Could not find business name" failures.
                var asicSearch = await _asicClient.SearchByAbnAsync(sale.Abn);
                if (!asicSearch.Success || asicSearch.BusinessNames == null || asicSearch.BusinessNames.Count == 0)
                {
                    sale.ErrorMessage = $"ASIC search failed: {asicSearch.ErrorMessage ?? "no business names returned"}";
                    sale.Status = OntraportSaleStatusClassifier.ClassifyError(asicSearch.ErrorMessage);
                    _logger.LogWarning("ASIC search failed for ABN {Abn} → {Status}: {Message}", sale.Abn, sale.Status, sale.ErrorMessage);
                    continue;
                }

                var matched = asicSearch.BusinessNames.FirstOrDefault(b =>
                    string.Equals(
                        (b.Name ?? "").Trim(),
                        (sale.BusinessName ?? "").Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (matched == null)
                {
                    sale.Status = OntraportSaleStatus.RenewalFailed;
                    sale.ErrorMessage = $"Business name '{sale.BusinessName}' not found under ABN {sale.Abn} at ASIC";
                    _logger.LogWarning("No ASIC match for {BusinessName} under ABN {Abn}", sale.BusinessName, sale.Abn);
                    continue;
                }

                // Create SearchLog and SearchResult for this ABN with the real ASIC account number
                var searchLog = new SearchLog
                {
                    Id = Guid.NewGuid(),
                    Abn = sale.Abn,
                    SearchedAt = DateTime.UtcNow,
                    Success = true,
                    ResultsCount = 1,
                    InitiatedBy = SearchInitiator.System
                };
                _dbContext.SearchLogs.Add(searchLog);

                var searchResult = new SearchResult
                {
                    Id = Guid.NewGuid(),
                    SearchLogId = searchLog.Id,
                    BusinessName = matched.Name,
                    AccountNumber = matched.AccountNumber,
                    RegistrationDate = matched.RegistrationDate ?? sale.RenewalDueDate?.ToString("dd/MM/yyyy") ?? ""
                };
                _dbContext.SearchResults.Add(searchResult);

                // Create RenewalRequest
                var renewalRequest = new RenewalRequest
                {
                    Id = Guid.NewGuid(),
                    SearchResultId = searchResult.Id,
                    InitiatedAt = DateTime.UtcNow,
                    RenewalYears = sale.RenewalYears,
                    Email = sale.Email,
                    MobileNumber = sale.MobileNumber,
                    DateOfBirth = ParseDateOfBirth(sale.DateOfBirth),
                    PaymentType = PaymentType.External, // Already paid in Ontraport
                    Amount = sale.AmountPaid,
                    Status = RenewalStatus.Pending,
                    Source = RenewalSource.Ontraport
                };
                _dbContext.RenewalRequests.Add(renewalRequest);

                // Link sale to renewal request
                sale.RenewalRequestId = renewalRequest.Id;
                sale.Status = OntraportSaleStatus.RenewalQueued;

                await _dbContext.SaveChangesAsync();

                // Queue the ASIC renewal via Hangfire
                _backgroundJobClient.Enqueue<IRenewalProcessingService>(
                    service => service.ProcessRenewalAsync(renewalRequest.Id));

                _logger.LogInformation("Queued renewal {RenewalId} for Ontraport sale {SaleId}",
                    renewalRequest.Id, sale.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing renewal for Ontraport sale {SaleId}", sale.Id);
                sale.Status = OntraportSaleStatus.RenewalFailed;
                sale.ErrorMessage = ex.Message;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> SyncRenewalOutcomeAsync(string contactId, OntraportRenewalOutcome outcome, DateTime? newRenewalDueDate = null)
    {
        if (string.IsNullOrWhiteSpace(contactId))
            return true;

        var fields = new Dictionary<string, object>
        {
            ["id"] = contactId,
            [FieldRenewalStatus] = outcome.ToFieldValue(),
        };

        // Only a completed renewal moves the due date. "Not Yet Due" means ASIC's window
        // hasn't opened (the date we hold is too early, not too late) and "Already Processed"
        // means someone else's renewal is still in flight — in both cases we'd be guessing.
        if (newRenewalDueDate is { } due)
            fields[FieldRenewalDueDate] = new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var fieldsOk = await UpdateContactFieldsAsync(contactId, fields);

        var tagOk = true;
        if (outcome == OntraportRenewalOutcome.Successful)
            tagOk = await AddContactTagAsync(contactId, RenewalCompletedTagId);

        return fieldsOk && tagOk;
    }

    public async Task<int> ProcessSyncOutboxAsync()
    {
        var pending = await _dbContext.OntraportSyncOutbox
            .Where(o => o.SentAt == null && o.AttemptCount < 10)
            .OrderBy(o => o.CreatedAt)
            .Take(100)
            .ToListAsync();

        var sent = 0;
        foreach (var entry in pending)
        {
            entry.AttemptCount++;
            entry.LastAttemptAt = DateTime.UtcNow;

            var ok = await SyncRenewalOutcomeAsync(entry.OntraportContactId, entry.Outcome, entry.NewRenewalDueDate);
            if (ok)
            {
                entry.SentAt = DateTime.UtcNow;
                entry.LastError = null;
                sent++;
            }
            else
            {
                entry.LastError = "One or more Ontraport writes failed; see server logs.";
            }

            await _dbContext.SaveChangesAsync();
        }

        if (pending.Count > 0)
            _logger.LogInformation("Ontraport sync outbox: sent {Sent} of {Pending} pending entries", sent, pending.Count);

        return sent;
    }

    private async Task<bool> UpdateContactFieldsAsync(string contactId, Dictionary<string, object> fields)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(fields),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PutAsync("Contacts", content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Ontraport field update for contact {ContactId} returned {StatusCode}: {Body}",
                    contactId, (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                return false;
            }

            _logger.LogInformation("Updated Ontraport contact {ContactId} fields: {Fields}",
                contactId, string.Join(", ", fields.Where(f => f.Key != "id").Select(f => $"{f.Key}={f.Value}")));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Ontraport contact {ContactId} fields", contactId);
            return false;
        }
    }

    private async Task<bool> AddContactTagAsync(string contactId, string tagId)
    {
        try
        {
            // Tags go through the tag endpoint, not the object-update one — a tag payload
            // PUT to /Contacts is rejected because it carries no field values.
            var content = new StringContent(
                JsonSerializer.Serialize(new { objectID = 0, ids = new[] { contactId }, add_list = new[] { tagId } }),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PutAsync("objects/tag", content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Ontraport tag add for contact {ContactId} returned {StatusCode}: {Body}",
                    contactId, (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tag Ontraport contact {ContactId}", contactId);
            return false;
        }
    }

    private async Task<List<Dictionary<string, string?>>> FetchPaidContactsAsync()
    {
        // Query contacts where f5194 (payment received) = "yes", sorted by most recent activity
        // Condition format: value must be wrapped in {"value":"..."} for Ontraport API
        var condition = Uri.EscapeDataString(
            "[{\"field\":{\"field\":\"f5194\"},\"op\":\"=\",\"value\":{\"value\":\"yes\"}}]");
        var fields = $"id,firstname,lastname,email,sms_number,spent,refund,refundtotal,{FieldBusinessName},{FieldAbn},{FieldBusinessNameOwner},{FieldRenewalDueDate},{FieldRenewalTerm},{FieldPaymentReceived},{FieldCancel},{FieldDateOfBirth}";

        // Fetch up to 1000 most recent paid contacts in a single call (sorted by last activity desc)
        // New renewals always appear near the top; already-synced ones are skipped in code
        var url = $"Contacts?condition={condition}&range=1000&listFields={fields}&sortDir=desc&sort=dla";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var data = doc.RootElement.GetProperty("data");

        var contacts = new List<Dictionary<string, string?>>();
        foreach (var contact in data.EnumerateArray())
        {
            var dict = new Dictionary<string, string?>();
            foreach (var prop in contact.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
            }
            contacts.Add(dict);
        }

        return contacts;
    }

    private async Task<Dictionary<string, string?>?> FetchContactByIdAsync(string contactId)
    {
        try
        {
            var fields = $"id,spent,refund,refundtotal,{FieldPaymentReceived},{FieldCancel}";
            var response = await _httpClient.GetAsync($"Contact?id={contactId}&listFields={fields}");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ontraport contact lookup for {ContactId} returned {StatusCode}", contactId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;

            var dict = new Dictionary<string, string?>();
            foreach (var prop in data.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
            }
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Ontraport contact {ContactId}", contactId);
            return null;
        }
    }

    private string? GetIneligibilityReason(Dictionary<string, string?> contact, decimal amountPaid)
    {
        // Payment must still read "yes" — David sets this to "dispute" when a refund is requested
        var paymentReceived = (contact.GetValueOrDefault(FieldPaymentReceived, "") ?? "").Trim();
        if (!string.Equals(paymentReceived, "yes", StringComparison.OrdinalIgnoreCase))
            return $"Stripe Payment Recieved is '{paymentReceived}' (expected 'yes')";

        // Customer marked the business name (or ABN+name) for cancellation rather than renewal
        var cancelValue = (contact.GetValueOrDefault(FieldCancel, "") ?? "").Trim();
        if (!string.IsNullOrEmpty(cancelValue) && cancelValue != "0")
            return "Contact has the Cancel field set — customer is cancelling, not renewing";

        // Ontraport's native refund rollup: >0 means Stripe issued a refund/chargeback for this contact
        var refundCountStr = (contact.GetValueOrDefault("refund", "0") ?? "0").Trim();
        if (decimal.TryParse(refundCountStr, out var refundCount) && refundCount > 0)
            return $"Contact has {refundCount} refund(s) on file";

        // Underpayment guard — customers paying only the $39/$49 cancellation fee fall below the one-year renewal price
        var minimumFee = _pricingSettings.Value.OneYearFee;
        if (minimumFee > 0 && amountPaid < minimumFee)
            return $"Amount paid ({amountPaid:C}) is below the minimum renewal fee ({minimumFee:C})";

        return null;
    }

    private static DateOnly? ParseDateOfBirth(string? dob)
    {
        if (string.IsNullOrWhiteSpace(dob)) return null;

        // Try dd/MM/yyyy format (common in Ontraport AU)
        if (DateOnly.TryParseExact(dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        // Try other common formats
        if (DateOnly.TryParseExact(dob, "d/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;

        if (DateOnly.TryParse(dob, out date))
            return date;

        return null;
    }
}

