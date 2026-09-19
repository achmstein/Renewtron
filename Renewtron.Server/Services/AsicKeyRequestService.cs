using System.Text.RegularExpressions;
using Asic.Client.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// Sale → ASIC enquiry form → (ASIC emails the key) → inbox notification. Rows are created
/// by the sales sync or by hand, submitted here in capped batches, and closed off when the
/// inbox scanner has logged a notification carrying the same business name.
/// </summary>
public sealed class AsicKeyRequestService : IAsicKeyRequestService
{
    // Transient failures (low captcha score, ASIC hiccup) get this many goes in total, with a
    // linear back-off (attempt N waits N hours) so a bad night at ASIC doesn't burn captcha
    // credit every half hour.
    private const int MaxAutoAttempts = 3;

    private readonly ApplicationDbContext _db;
    private readonly IAsicKeyRequestClient _client;
    private readonly IOptionsMonitor<AsicKeyRequestSettings> _settings;
    private readonly ILogger<AsicKeyRequestService> _logger;

    public AsicKeyRequestService(
        ApplicationDbContext db,
        IAsicKeyRequestClient client,
        IOptionsMonitor<AsicKeyRequestSettings> settings,
        ILogger<AsicKeyRequestService> logger)
    {
        _db = db;
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AsicKeyRequestRunResult> ProcessPendingAsync(CancellationToken ct = default)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            return new AsicKeyRequestRunResult(true, "ASIC key requests are disabled in settings.", 0, 0, 0);
        if (string.IsNullOrWhiteSpace(settings.TwoCaptchaApiKey))
            return new AsicKeyRequestRunResult(true, "2Captcha API key is not configured (Settings → ASIC key requests).", 0, 0, 0);

        // 1. Close the loop on anything whose key has since arrived.
        var matched = await MatchReceivedKeysAsync(ct);

        // 2. Submit what's due, newest sales last so a backlog drains in order.
        var now = DateTime.UtcNow;
        var due = await _db.AsicKeyRequests
            .Where(r => r.Status == AsicKeyRequestStatus.Pending
                     || (r.Status == AsicKeyRequestStatus.Failed && r.CanAutoRetry && r.AttemptCount < MaxAutoAttempts
                         && (r.ProcessedAt == null || EF.Functions.DateDiffHour(r.ProcessedAt.Value, now) >= r.AttemptCount)))
            .OrderBy(r => r.CreatedAt)
            .Take(Math.Clamp(settings.MaxPerRun, 1, 500))
            .ToListAsync(ct);

        int submitted = 0, failed = 0;
        foreach (var row in due)
        {
            ct.ThrowIfCancellationRequested();
            await SubmitRowAsync(row, settings, ct);
            await _db.SaveChangesAsync(ct);
            if (row.Status == AsicKeyRequestStatus.Submitted) submitted++; else failed++;
        }

        var summary = due.Count == 0 && matched == 0
            ? "Nothing to send; no new keys arrived."
            : $"Submitted {submitted} to ASIC, {failed} failed; {matched} key(s) matched to earlier requests.";
        _logger.LogInformation("ASIC key request run: {Summary}", summary);
        return new AsicKeyRequestRunResult(false, summary, submitted, failed, matched);
    }

    public async Task<AsicKeyRequest?> SubmitAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.AsicKeyRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row == null) return null;

        await SubmitRowAsync(row, _settings.CurrentValue, ct);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<AsicKeyRequest> QueueForSaleAsync(Guid saleId, bool submitNow, CancellationToken ct = default)
    {
        var sale = await _db.OntraportSales.FirstOrDefaultAsync(s => s.Id == saleId, ct)
                   ?? throw new KeyNotFoundException($"No Ontraport sale {saleId}.");

        // Re-use an open row for the sale rather than stacking duplicates; a request that
        // already went through gets a fresh row so its reference number stays on record.
        var open = await _db.AsicKeyRequests
            .Where(r => r.OntraportSaleId == saleId
                     && (r.Status == AsicKeyRequestStatus.Pending || r.Status == AsicKeyRequestStatus.Failed))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        AsicKeyRequest row;
        if (open != null)
        {
            row = open;
            row.Status = AsicKeyRequestStatus.Pending;
            row.ErrorMessage = null;
            row.CanAutoRetry = true;
            row.Source = "Manual";
        }
        else
        {
            row = FromSale(sale, "Manual");
            _db.AsicKeyRequests.Add(row);
        }

        if (submitNow)
            await SubmitRowAsync(row, _settings.CurrentValue, ct);

        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Builds the request row for a sale: name split, phone and ABN snapshotted.</summary>
    public static AsicKeyRequest FromSale(OntraportSale sale, string source)
    {
        var (given, family) = SplitName(sale.ContactName);
        return new AsicKeyRequest
        {
            Id = Guid.NewGuid(),
            OntraportSaleId = sale.Id,
            OntraportContactId = sale.OntraportContactId,
            GivenNames = given,
            FamilyName = family,
            Phone = sale.MobileNumber,
            Abn = DigitsOnly(sale.Abn),
            BusinessName = sale.BusinessName.Trim(),
            Source = source,
            Status = AsicKeyRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private async Task SubmitRowAsync(AsicKeyRequest row, AsicKeyRequestSettings settings, CancellationToken ct)
    {
        row.AttemptCount++;
        row.ProcessedAt = DateTime.UtcNow;
        row.ErrorMessage = null;

        var email = (settings.RequestEmail ?? "").Trim();
        if (email.Length == 0)
        {
            Fail(row, "Request email is not configured (Settings → ASIC key requests).", canRetry: false);
            return;
        }

        var (prefix, number) = ResolvePhone(row.Phone, settings);
        row.Question = Render(string.IsNullOrWhiteSpace(settings.MessageTemplate) ? AsicKeyRequestSettings.DefaultMessageTemplate : settings.MessageTemplate, row, email);

        var input = new AsicKeyRequestInput
        {
            GivenNames = row.GivenNames,
            FamilyName = row.FamilyName,
            Abn = row.Abn,
            BusinessName = row.BusinessName,
            Email = email,
            PhonePrefix = prefix,
            PhoneNumber = number,
            Question = row.Question,
        };

        AsicKeyRequestResult result;
        try
        {
            result = await _client.SubmitAsync(input, settings.MinCaptchaScore, Math.Clamp(settings.MaxCaptchaAttempts, 1, 5), settings.ProxyUrl, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASIC key request {Id} ({BusinessName}) threw", row.Id, row.BusinessName);
            Fail(row, $"Unexpected error: {ex.Message}", canRetry: true);
            return;
        }

        row.CaptchaSolves += result.CaptchaAttempts;
        if (result.Success)
        {
            row.Status = AsicKeyRequestStatus.Submitted;
            row.SubmittedAt = DateTime.UtcNow;
            row.AsicReferenceNumber = result.ReferenceNumber;
            row.ErrorMessage = null;
            row.CanAutoRetry = false;
            _logger.LogInformation("ASIC key request for '{BusinessName}' (ABN {Abn}) submitted, reference {Reference}, {Solves} captcha solve(s)",
                row.BusinessName, row.Abn, result.ReferenceNumber, result.CaptchaAttempts);
        }
        else
        {
            Fail(row, result.ErrorMessage ?? "ASIC submission failed.", canRetry: result.Transient);
        }
    }

    private void Fail(AsicKeyRequest row, string error, bool canRetry)
    {
        row.Status = AsicKeyRequestStatus.Failed;
        row.ErrorMessage = error.Length <= 1000 ? error : error[..1000];
        row.CanAutoRetry = canRetry;
        _logger.LogWarning("ASIC key request {Id} ('{BusinessName}') failed{Retry}: {Error}",
            row.Id, row.BusinessName, canRetry ? " (will retry)" : "", error);
    }

    /// <summary>
    /// Submitted requests whose business name has since shown up on an inbox notification
    /// with a key become KeyReceived. Names are matched exactly (case-insensitive) — the
    /// same rule the inbox uses against Ontraport — and only notifications received after
    /// the request went out count, so a stale letter can't close a fresh request.
    /// </summary>
    private async Task<int> MatchReceivedKeysAsync(CancellationToken ct)
    {
        var submitted = await _db.AsicKeyRequests
            .Where(r => r.Status == AsicKeyRequestStatus.Submitted && r.SubmittedAt != null)
            .ToListAsync(ct);
        if (submitted.Count == 0) return 0;

        var since = submitted.Min(r => r.SubmittedAt!.Value).AddDays(-1);
        var notifications = await _db.AsicKeyNotifications
            .Where(n => n.AsicKey != null && n.BusinessName != null && n.ReceivedAt >= since)
            .Select(n => new { n.Id, n.BusinessName, n.ReceivedAt })
            .ToListAsync(ct);
        if (notifications.Count == 0) return 0;

        var matched = 0;
        foreach (var row in submitted)
        {
            var hit = notifications
                .Where(n => string.Equals(n.BusinessName!.Trim(), row.BusinessName.Trim(), StringComparison.OrdinalIgnoreCase)
                         && n.ReceivedAt >= row.SubmittedAt!.Value.AddDays(-1))
                .OrderBy(n => n.ReceivedAt)
                .FirstOrDefault();
            if (hit == null) continue;

            row.Status = AsicKeyRequestStatus.KeyReceived;
            row.KeyReceivedAt = hit.ReceivedAt;
            row.AsicKeyNotificationId = hit.Id;
            matched++;
            _logger.LogInformation("ASIC key for '{BusinessName}' arrived {ReceivedAt:u}; request {Id} closed", row.BusinessName, hit.ReceivedAt, row.Id);
        }
        if (matched > 0) await _db.SaveChangesAsync(ct);
        return matched;
    }

    // ---- helpers ----------------------------------------------------------------------

    private static (string Given, string Family) SplitName(string? fullName)
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
    /// from Ontraport arrive as "0412 345 678", "+61412345678" or "61412345678"; anything
    /// that doesn't normalise to ten digits falls back to the configured office number.
    /// </summary>
    private static (string Prefix, string Number) ResolvePhone(string? raw, AsicKeyRequestSettings settings)
    {
        var digits = DigitsOnly(raw);
        if (digits.StartsWith("61") && digits.Length == 11) digits = "0" + digits[2..];
        if (digits.Length == 10 && digits[0] == '0')
            return (digits[..2], digits[2..]);
        if (digits.Length is 8 or 9)
            return ((settings.DefaultPhonePrefix ?? "").Trim(), digits);
        return ((settings.DefaultPhonePrefix ?? "").Trim(), DigitsOnly(settings.DefaultPhoneNumber));
    }

    private static string Render(string template, AsicKeyRequest row, string email)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = row.GivenNames,
            ["LastName"] = row.FamilyName,
            ["Abn"] = row.Abn,
            ["BusinessName"] = row.BusinessName,
            ["Email"] = email,
        };
        var text = Regex.Replace(template, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return Regex.Replace(text, @"[ \t]+", " ").Trim();
    }

    private static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
}
