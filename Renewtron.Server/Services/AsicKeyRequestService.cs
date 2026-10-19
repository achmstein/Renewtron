using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// Sale → queued ASIC key request → a person fills in ASIC's form and records the reference
/// → (inbox scanner) → key on the contact. The server owns the queue and the audit trail;
/// it never submits the form itself, because ASIC's captcha refuses its address.
/// </summary>
public sealed class AsicKeyRequestService : IAsicKeyRequestService
{
    private readonly ApplicationDbContext _db;
    private readonly IOptionsMonitor<AsicKeyRequestSettings> _settings;
    private readonly ILogger<AsicKeyRequestService> _logger;

    public AsicKeyRequestService(
        ApplicationDbContext db,
        IOptionsMonitor<AsicKeyRequestSettings> settings,
        ILogger<AsicKeyRequestService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    // ---- queueing -------------------------------------------------------------------

    /// <summary>
    /// A request row snapshotted from a sale, with the enquiry text already rendered, so a
    /// later contact edit can't change what the person is told to send.
    /// </summary>
    public static AsicKeyRequest FromSale(OntraportSale sale, string source, AsicKeyRequestSettings settings)
    {
        var (given, family) = SplitName(sale.ContactName);
        var request = new AsicKeyRequest
        {
            Id = Guid.NewGuid(),
            OntraportSaleId = sale.Id,
            OntraportContactId = sale.OntraportContactId,
            GivenNames = given,
            FamilyName = family,
            Email = (sale.Email ?? "").Trim(),
            Phone = sale.MobileNumber,
            Abn = DigitsOnly(sale.Abn),
            BusinessName = sale.BusinessName.Trim(),
            DateOfBirth = FormatDateOfBirth(sale.DateOfBirth),
            Address = JoinAddress(sale.Address, sale.Suburb, sale.State, sale.Postcode),
            Source = source,
            Status = AsicKeyRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        request.Question = Render(request, settings);
        return request;
    }

    public async Task<AsicKeyRequest> QueueForSaleAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await _db.OntraportSales.FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new InvalidOperationException("Sale not found.");

        // One live request per sale: re-open a failed one rather than stacking duplicates.
        var request = await _db.AsicKeyRequests
            .Where(r => r.OntraportSaleId == saleId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (request == null)
        {
            request = FromSale(sale, "Manual", _settings.CurrentValue);
            _db.AsicKeyRequests.Add(request);
        }
        else if (request.Status == AsicKeyRequestStatus.Failed)
        {
            request.Status = AsicKeyRequestStatus.Pending;
            request.ErrorMessage = null;
            request.CanAutoRetry = true;
        }
        await _db.SaveChangesAsync(ct);
        return request;
    }

    // ---- housekeeping ---------------------------------------------------------------

    public async Task<AsicKeyRequestRunResult> ProcessPendingAsync(CancellationToken ct = default)
    {
        if (!_settings.CurrentValue.Enabled)
            return new AsicKeyRequestRunResult(true, "ASIC key requests are disabled (Settings → ASIC key requests).", 0, 0);

        var matched = await MatchArrivedKeysAsync(ct);

        // A row a person took but never closed off goes back into the queue after a day.
        var stale = DateTime.UtcNow.AddHours(-24);
        var abandoned = await _db.AsicKeyRequests
            .Where(r => r.Status == AsicKeyRequestStatus.Manual && (r.ProcessedAt ?? r.CreatedAt) < stale)
            .ToListAsync(ct);
        foreach (var row in abandoned)
        {
            row.Status = AsicKeyRequestStatus.Pending;
            row.ErrorMessage = null;
        }
        if (abandoned.Count > 0) await _db.SaveChangesAsync(ct);

        var message = $"{matched} key(s) matched from the inbox, {abandoned.Count} request(s) put back in the queue.";
        _logger.LogInformation("ASIC key request housekeeping: {Message}", message);
        return new AsicKeyRequestRunResult(false, message, matched, abandoned.Count);
    }

    /// <summary>
    /// Submitted requests whose business name (or ABN) has since turned up in an inbox
    /// notification carrying a key are closed off as KeyReceived, linked to that notification.
    /// </summary>
    private async Task<int> MatchArrivedKeysAsync(CancellationToken ct)
    {
        var waiting = await _db.AsicKeyRequests
            .Where(r => r.Status == AsicKeyRequestStatus.Submitted)
            .ToListAsync(ct);
        if (waiting.Count == 0) return 0;

        var since = waiting.Min(r => r.CreatedAt).AddDays(-1);
        var arrived = await _db.AsicKeyNotifications
            .Where(n => n.AsicKey != null && n.BusinessName != null && n.ReceivedAt >= since)
            .Select(n => new { n.Id, n.BusinessName, n.Abn, n.ReceivedAt })
            .ToListAsync(ct);

        var matched = 0;
        foreach (var request in waiting)
        {
            var hit = arrived
                .Where(n => n.ReceivedAt >= request.CreatedAt.AddDays(-1))
                .FirstOrDefault(n =>
                    string.Equals(Normalise(n.BusinessName), Normalise(request.BusinessName), StringComparison.OrdinalIgnoreCase)
                    || (n.Abn != null && DigitsOnly(n.Abn) == request.Abn));
            if (hit == null) continue;

            request.Status = AsicKeyRequestStatus.KeyReceived;
            request.AsicKeyNotificationId = hit.Id;
            request.KeyReceivedAt = hit.ReceivedAt;
            matched++;
        }
        if (matched > 0) await _db.SaveChangesAsync(ct);
        return matched;
    }

    // ---- form values ----------------------------------------------------------------

    /// <summary>The enquiry text for the form's "My question is" box.</summary>
    public static string Render(AsicKeyRequest request, AsicKeyRequestSettings settings)
    {
        var template = string.IsNullOrWhiteSpace(settings.MessageTemplate) ? AsicKeyRequestSettings.DefaultMessageTemplate : settings.MessageTemplate;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = request.GivenNames.Trim(),
            ["LastName"] = request.FamilyName.Trim(),
            ["Abn"] = DigitsOnly(request.Abn),
            ["BusinessName"] = request.BusinessName.Trim(),
            ["Email"] = (settings.RequestEmail ?? "").Trim(),
            ["ClientEmail"] = request.Email.Trim(),
            ["DateOfBirth"] = (request.DateOfBirth ?? "").Trim(),
            ["Address"] = (request.Address ?? "").Trim(),
        };
        return Fill(template, values);
    }

    /// <summary>Optional placeholders: a comma-separated part of the template that holds one with
    /// no value is dropped whole, so "…, date of birth {DateOfBirth}, …" doesn't leave a dangling label.</summary>
    private static readonly string[] OptionalPlaceholders = ["DateOfBirth", "Address"];

    private static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        var parts = template.Split(',').Where(part => !OptionalPlaceholders.Any(name =>
            part.Contains($"{{{name}}}", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(values.GetValueOrDefault(name))));
        var text = Regex.Replace(string.Join(",", parts), @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return Regex.Replace(text, @"[ \t]+", " ").Trim();
    }

    /// <summary>A date of birth in whatever shape it arrived (yyyy-MM-dd, dd/MM/yyyy, unix
    /// seconds from Ontraport) as dd/MM/yyyy; null when there isn't one.</summary>
    public static string? FormatDateOfBirth(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return null;
        var inv = CultureInfo.InvariantCulture;
        if (Regex.IsMatch(value, @"^\d{9,11}$") && long.TryParse(value, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("dd/MM/yyyy", inv);
        string[] formats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy"];
        if (DateTime.TryParseExact(value, formats, inv, DateTimeStyles.None, out var date))
            return date.ToString("dd/MM/yyyy", inv);
        return value;
    }

    /// <summary>"1 Smith St, Suburb NSW 2000" from the parts we have; null when there are none.</summary>
    public static string? JoinAddress(string? street, string? suburb, string? state, string? postcode)
    {
        static string Clean(string? v) => Regex.Replace(v ?? "", @"\s+", " ").Trim();
        var locality = string.Join(" ", new[] { Clean(suburb), Clean(state), Clean(postcode) }.Where(v => v.Length > 0));
        var joined = string.Join(", ", new[] { Clean(street), locality }.Where(v => v.Length > 0));
        return joined.Length == 0 ? null : joined;
    }

    public static (string Given, string Family) SplitName(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (parts[0], parts[0]),
            _ => (string.Join(' ', parts[..^1]), parts[^1]),
        };
    }

    /// <summary>
    /// ASIC wants an area/prefix box (max 4) and a number box (max 15). Australian numbers
    /// arrive as "0412 345 678", "+61412345678" or "61412345678"; anything that doesn't
    /// normalise to ten digits falls back to the configured office number.
    /// </summary>
    public static (string Prefix, string Number) ResolvePhone(string? raw, AsicKeyRequestSettings settings)
    {
        var digits = DigitsOnly(raw);
        if (digits.StartsWith("61") && digits.Length == 11) digits = "0" + digits[2..];
        if (digits.Length == 10 && digits[0] == '0')
            return (digits[..2], digits[2..]);
        if (digits.Length is 8 or 9)
            return ((settings.DefaultPhonePrefix ?? "").Trim(), digits);
        return ((settings.DefaultPhonePrefix ?? "").Trim(), DigitsOnly(settings.DefaultPhoneNumber));
    }

    private static string Normalise(string? name) => Regex.Replace(name ?? "", @"\s+", " ").Trim();

    public static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
}
