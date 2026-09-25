using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// Sale → ASIC key request → browser submission → (inbox scanner) → key on the contact.
/// Runs are sequential on purpose: one browser at a time, one ASIC session per enquiry.
/// </summary>
public sealed class AsicKeyRequestService : IAsicKeyRequestService
{
    private readonly ApplicationDbContext _db;
    private readonly AsicEnquiryBrowser _browser;
    private readonly IOptionsMonitor<AsicKeyRequestSettings> _settings;
    private readonly HttpClient _http;
    private readonly ILogger<AsicKeyRequestService> _logger;

    public AsicKeyRequestService(
        ApplicationDbContext db,
        AsicEnquiryBrowser browser,
        IOptionsMonitor<AsicKeyRequestSettings> settings,
        HttpClient http,
        ILogger<AsicKeyRequestService> logger)
    {
        _db = db;
        _browser = browser;
        _settings = settings;
        _http = http;
        _logger = logger;
    }

    // ---- queueing -------------------------------------------------------------------

    /// <summary>A request row snapshotted from a sale, so a later contact edit can't change what we told ASIC.</summary>
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
            Email = (sale.Email ?? "").Trim(),
            Phone = sale.MobileNumber,
            Abn = DigitsOnly(sale.Abn),
            BusinessName = sale.BusinessName.Trim(),
            Source = source,
            Status = AsicKeyRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public async Task<AsicKeyRequest> QueueForSaleAsync(Guid saleId, bool submitNow, CancellationToken ct = default)
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
            request = FromSale(sale, "Manual");
            _db.AsicKeyRequests.Add(request);
        }
        else if (request.Status == AsicKeyRequestStatus.Failed)
        {
            request.Status = AsicKeyRequestStatus.Pending;
            request.ErrorMessage = null;
            request.CanAutoRetry = true;
        }
        await _db.SaveChangesAsync(ct);

        if (submitNow)
            return await SubmitAsync(request.Id, ct) ?? request;
        return request;
    }

    // ---- the run --------------------------------------------------------------------

    public async Task<AsicKeyRequestRunResult> ProcessPendingAsync(CancellationToken ct = default)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            return new AsicKeyRequestRunResult(true, "ASIC key requests are disabled (Settings → ASIC key requests).", 0, 0, 0);

        var matched = await MatchArrivedKeysAsync(ct);

        var due = await _db.AsicKeyRequests
            .Where(r => r.Status == AsicKeyRequestStatus.Pending
                || (r.Status == AsicKeyRequestStatus.Failed && r.CanAutoRetry && r.AttemptCount < settings.MaxAutoAttempts))
            .OrderBy(r => r.Status == AsicKeyRequestStatus.Pending ? 0 : 1)
            .ThenBy(r => r.CreatedAt)
            .Take(Math.Max(1, settings.MaxPerRun))
            .ToListAsync(ct);

        var submitted = 0;
        var failed = 0;
        var consecutiveCaptchaFailures = 0;
        var stoppedEarly = false;
        for (var i = 0; i < due.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.PauseBetweenRequestsSeconds)), ct);

            var request = due[i];
            var result = await SubmitCoreAsync(request, settings, ct);
            if (request.Status == AsicKeyRequestStatus.Submitted) submitted++; else failed++;

            // A burst of visits from one address is scored progressively lower. Once ASIC has
            // bounced two in a row, the rest of the queue only makes it worse — leave them for
            // the next run, when the score has had time to recover.
            consecutiveCaptchaFailures = result?.CaptchaRejected == true ? consecutiveCaptchaFailures + 1 : 0;
            if (consecutiveCaptchaFailures >= Math.Max(1, settings.StopRunAfterCaptchaFailures) && i < due.Count - 1)
            {
                stoppedEarly = true;
                _logger.LogWarning("Stopping the ASIC key request run after {Count} consecutive captcha rejections; {Left} left for the next run", consecutiveCaptchaFailures, due.Count - 1 - i);
                break;
            }
        }

        var message = due.Count == 0
            ? $"Nothing to send. {matched} key(s) matched from the inbox."
            : $"{submitted} submitted, {failed} failed, {matched} key(s) matched from the inbox." +
              (stoppedEarly ? " Stopped early: ASIC is scoring this connection low; the rest go out next run." : "");
        _logger.LogInformation("ASIC key request run: {Message}", message);
        return new AsicKeyRequestRunResult(false, message, submitted, failed, matched);
    }

    public async Task<AsicKeyRequest?> SubmitAsync(Guid id, CancellationToken ct = default)
    {
        var request = await _db.AsicKeyRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request == null) return null;
        await SubmitCoreAsync(request, _settings.CurrentValue, ct);
        return request;
    }

    private async Task<AsicEnquiryResult?> SubmitCoreAsync(AsicKeyRequest request, AsicKeyRequestSettings settings, CancellationToken ct)
    {
        request.AttemptCount++;
        request.ProcessedAt = DateTime.UtcNow;

        if (Problem(request, settings) is { } problem)
        {
            request.Status = AsicKeyRequestStatus.Failed;
            request.ErrorMessage = problem;
            request.CanAutoRetry = false;
            await _db.SaveChangesAsync(ct);
            return null;
        }

        var input = ToInput(request, settings);
        request.Question = input.Question;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Submitting ASIC key request for {BusinessName} (ABN {Abn}) via {Browser}",
            request.BusinessName, request.Abn, AsicEnquiryBrowser.BrowserName);

        var result = await _browser.SubmitAsync(input, settings.MaxCaptchaAttempts, ct);
        request.CaptchaSolves += result.Attempts;
        if (result.Success)
        {
            request.Status = AsicKeyRequestStatus.Submitted;
            request.AsicReferenceNumber = result.ReferenceNumber;
            request.SubmittedAt = DateTime.UtcNow;
            request.ErrorMessage = null;
            request.CanAutoRetry = true;
            _logger.LogInformation("ASIC accepted the enquiry for {BusinessName}: reference {Reference}", request.BusinessName, result.ReferenceNumber);
        }
        else
        {
            request.Status = AsicKeyRequestStatus.Failed;
            request.ErrorMessage = Truncate(result.ErrorMessage ?? "failed", 1000);
            request.CanAutoRetry = result.Transient;
            _logger.LogWarning("ASIC key request for {BusinessName} failed: {Error}", request.BusinessName, result.ErrorMessage);
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// Submitted requests whose business name has since turned up in a completed inbox
    /// notification are closed off as KeyReceived, linked to that notification.
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

    // ---- probe ----------------------------------------------------------------------

    public async Task<AsicKeyRequestProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        string? ip = null;
        try
        {
            using var response = await _http.GetAsync("https://api.ipify.org", ct);
            if (response.IsSuccessStatusCode) ip = (await response.Content.ReadAsStringAsync(ct)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* informational */ }

        var (ok, error) = await _browser.ProbeAsync(ct);
        return new AsicKeyRequestProbeResult(ok, AsicEnquiryBrowser.BrowserName, ip, error);
    }

    // ---- form values ----------------------------------------------------------------

    private static string? Problem(AsicKeyRequest request, AsicKeyRequestSettings settings)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName)) return "Business name is blank.";
        if (string.IsNullOrWhiteSpace(request.GivenNames) || string.IsNullOrWhiteSpace(request.FamilyName)) return "Contact name is blank.";
        var abn = DigitsOnly(request.Abn);
        if (abn.Length != 11) return abn.Length == 0 ? "ABN is blank." : $"ABN has {abn.Length} digits, expected 11.";
        if (!LooksLikeEmail(request.Email)) return request.Email.Trim().Length == 0 ? "Contact has no email address (ASIC's reply-to)." : "Contact email isn't an address.";
        if (ResolvePhone(request.Phone, settings).Number.Length == 0)
            return "Contact has no phone number and no fallback phone is set (Settings → ASIC key requests).";
        if (string.IsNullOrWhiteSpace(settings.RequestEmail)) return "No key delivery email is set (Settings → ASIC key requests).";
        return null;
    }

    private static AsicEnquiryInput ToInput(AsicKeyRequest request, AsicKeyRequestSettings settings)
    {
        var (prefix, number) = ResolvePhone(request.Phone, settings);
        return new AsicEnquiryInput
        {
            GivenNames = request.GivenNames.Trim(),
            FamilyName = request.FamilyName.Trim(),
            Abn = DigitsOnly(request.Abn),
            BusinessName = request.BusinessName.Trim(),
            Email = request.Email.Trim(),
            PhonePrefix = prefix,
            PhoneNumber = number,
            Question = Render(request, settings),
        };
    }

    private static string Render(AsicKeyRequest request, AsicKeyRequestSettings settings)
    {
        var template = string.IsNullOrWhiteSpace(settings.MessageTemplate) ? AsicKeyRequestSettings.DefaultMessageTemplate : settings.MessageTemplate;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = request.GivenNames.Trim(),
            ["LastName"] = request.FamilyName.Trim(),
            ["Abn"] = DigitsOnly(request.Abn),
            ["BusinessName"] = request.BusinessName.Trim(),
            ["Email"] = settings.RequestEmail.Trim(),
            ["ClientEmail"] = request.Email.Trim(),
        };
        var text = Regex.Replace(template, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return Regex.Replace(text, @"[ \t]+", " ").Trim();
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

    private static bool LooksLikeEmail(string? value)
    {
        var v = (value ?? "").Trim();
        var at = v.IndexOf('@');
        return at > 0 && at < v.Length - 1 && !v.Contains(' ') && v.IndexOf('.', at) > at + 1;
    }

    private static string Normalise(string? name) => Regex.Replace(name ?? "", @"\s+", " ").Trim();

    private static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
