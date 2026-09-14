using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Renewtron.Services;

/// <summary>
/// Inbox → "Click here" link → PDF → ASIC key → Ontraport contact field. Every email gets
/// an <see cref="AsicKeyNotification"/> row that records how far it got; the admin page
/// reads those rows and can retry or hand-assign a contact.
/// </summary>
public sealed class AsicKeyInboxService : IAsicKeyInboxService
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationDbContext _db;
    private readonly IAsicNotificationMailbox _mailbox;
    private readonly IOntraportSalesService _ontraport;
    private readonly IOptionsMonitor<AsicKeyInboxSettings> _settings;
    private readonly ILogger<AsicKeyInboxService> _logger;

    // Ontraport custom field IDs used for matching (same ones OntraportSalesService reads).
    private const string FieldBusinessName = "f5062";
    private const string FieldAbn = "f5063";

    // Transient failures are re-attempted on later scans up to this many times; after that
    // the row waits for a manual retry from the admin.
    private const int MaxAutoAttempts = 5;
    private const int PdfExcerptLength = 2000;

    // "You have requested a copy of a notification for BRM BUILDING REPAIRS & MAINTENANCE."
    // The name may itself contain periods, so the sentence only ends at a period that is
    // followed by a line break, the end of text, or the next sentence's "Please".
    private static readonly Regex BusinessNameRegex = new(
        @"notification for\s+(.+?)(?:\.\s*(?:\r?\n|$|Please\b)|\r?\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AbnRegex = new(
        @"\bABN\b[^0-9]{0,20}(\d{2}\s?\d{3}\s?\d{3}\s?\d{3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // The letters themselves name the business too — "Here is the ASIC Key for YUMOHZY TRITS:",
    // "Business name renewal notice for 'M.R.A. CONCRETING'", "…renewed for 'BECKS BAKES'" —
    // a second source for the name when the email body didn't parse.
    private static readonly Regex PdfBusinessNameRegex = new(
        @"(?:ASIC\s*key\s+for\s+([^:]{1,200}):|(?:notice|renewed)\s+for\s+'([^']{1,200})')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockBreakRegex = new(@"<(br|/p|/div|/tr|/li|/h\d|/table)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AsicKeyInboxService(
        HttpClient httpClient,
        ApplicationDbContext db,
        IAsicNotificationMailbox mailbox,
        IOntraportSalesService ontraport,
        IOptionsMonitor<AsicKeyInboxSettings> settings,
        ILogger<AsicKeyInboxService> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _mailbox = mailbox;
        _ontraport = ontraport;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AsicKeyScanResult> ScanAsync(CancellationToken ct = default)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            return new AsicKeyScanResult(true, "ASIC key inbox scanning is disabled in settings.", 0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
        {
            _logger.LogWarning("ASIC key inbox: mailbox credentials not configured; skipping scan");
            return new AsicKeyScanResult(true, "Mailbox credentials are not configured (Settings → ASIC key inbox).", 0, 0, 0, 0);
        }

        // 1. Discover: anything in the lookback window we haven't already got a row for.
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, settings.LookbackDays));
        var known = await _db.AsicKeyNotifications.Select(n => n.MessageId).ToHashSetAsync(ct);

        IReadOnlyList<MailboxMessage> messages;
        try
        {
            messages = await _mailbox.FetchNewNotificationsAsync(since, known.Contains, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASIC key inbox: mailbox fetch failed");
            throw new InvalidOperationException($"Mailbox fetch failed: {ex.Message}", ex);
        }

        var discovered = 0;
        foreach (var message in messages.DistinctBy(m => m.MessageId))
        {
            var row = new AsicKeyNotification
            {
                Id = Guid.NewGuid(),
                MessageId = message.MessageId,
                Subject = Truncate(message.Subject, 300),
                From = Truncate(message.From, 300),
                ReceivedAt = message.ReceivedAt,
                CreatedAt = DateTime.UtcNow,
                Status = AsicKeyNotificationStatus.Pending,
            };
            ParseEmail(row, message);
            _db.AsicKeyNotifications.Add(row);
            discovered++;
            _logger.LogInformation("ASIC key inbox: new notification for '{BusinessName}' received {ReceivedAt:u}", row.BusinessName, row.ReceivedAt);
        }
        await _db.SaveChangesAsync(ct);

        // 2. Process: fresh rows plus transient failures that haven't exhausted their attempts.
        // Retries back off linearly (attempt N waits N hours) so five attempts span ~10 hours
        // rather than the next five ticks. Download links die after 30 days, so anything
        // older than the lookback isn't retried.
        var now = DateTime.UtcNow;
        var pending = await _db.AsicKeyNotifications
            .Where(n => n.Status == AsicKeyNotificationStatus.Pending
                     || (n.AttemptCount < MaxAutoAttempts && n.ReceivedAt >= since
                         && (n.ProcessedAt == null || EF.Functions.DateDiffHour(n.ProcessedAt.Value, now) >= n.AttemptCount)
                         && (n.Status == AsicKeyNotificationStatus.DownloadFailed
                          || n.Status == AsicKeyNotificationStatus.ContactNotFound
                          || n.Status == AsicKeyNotificationStatus.OntraportUpdateFailed)))
            .OrderBy(n => n.ReceivedAt)
            .Take(100)
            .ToListAsync(ct);

        int processed = 0, completed = 0, failed = 0;
        foreach (var row in pending)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessRowAsync(row, settings, ct);
            await _db.SaveChangesAsync(ct);
            processed++;
            if (row.Status == AsicKeyNotificationStatus.Completed) completed++; else failed++;
        }

        var summary = discovered == 0 && processed == 0
            ? "Nothing new in the inbox."
            : $"{discovered} new email(s); processed {processed}: {completed} completed, {failed} need attention.";
        _logger.LogInformation("ASIC key inbox scan: {Summary}", summary);
        return new AsicKeyScanResult(false, summary, discovered, processed, completed, failed);
    }

    public async Task<AsicKeyNotification?> ProcessAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.AsicKeyNotifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (row == null) return null;

        await ProcessRowAsync(row, _settings.CurrentValue, ct);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<AsicKeyNotification?> ApplyToContactAsync(Guid id, string contactId, CancellationToken ct = default)
    {
        var row = await _db.AsicKeyNotifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (row == null) return null;
        if (string.IsNullOrWhiteSpace(row.AsicKey))
            throw new InvalidOperationException("This notification has no ASIC key yet — retry it first so the PDF is parsed.");

        row.AttemptCount++;
        row.ProcessedAt = DateTime.UtcNow;
        await WriteKeyAsync(row, [contactId.Trim()], _settings.CurrentValue);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    private async Task ProcessRowAsync(AsicKeyNotification row, AsicKeyInboxSettings settings, CancellationToken ct)
    {
        row.AttemptCount++;
        row.ProcessedAt = DateTime.UtcNow;
        row.ErrorMessage = null;

        try
        {
            // A previous attempt may already have the key (e.g. the contact was missing at the
            // time); don't hit ASIC again for it.
            if (string.IsNullOrWhiteSpace(row.AsicKey))
            {
                if (string.IsNullOrWhiteSpace(row.DownloadUrl))
                {
                    // Re-read the email — the link parser may have been improved since discovery.
                    var message = await _mailbox.FetchByMessageIdAsync(row.MessageId, ct);
                    if (message != null) ParseEmail(row, message);
                    if (string.IsNullOrWhiteSpace(row.DownloadUrl))
                    {
                        Fail(row, AsicKeyNotificationStatus.LinkNotFound, "No download link found in the email body.");
                        return;
                    }
                }

                var (pdf, downloadError) = await DownloadPdfAsync(row.DownloadUrl, ct);
                if (downloadError != null)
                {
                    Fail(row, AsicKeyNotificationStatus.DownloadFailed, downloadError);
                    return;
                }

                string text;
                try
                {
                    text = ExtractPdfText(pdf);
                }
                catch (Exception ex)
                {
                    Fail(row, AsicKeyNotificationStatus.KeyNotFound, $"PDF could not be parsed: {ex.Message}");
                    return;
                }
                row.PdfTextExcerpt = Truncate(text, PdfExcerptLength);

                string? key;
                try
                {
                    key = MatchKey(text, settings.AsicKeyPattern);
                }
                catch (ArgumentException ex)
                {
                    Fail(row, AsicKeyNotificationStatus.KeyNotFound, $"Invalid ASIC key pattern in settings: {ex.Message}");
                    return;
                }
                if (key == null)
                {
                    Fail(row, AsicKeyNotificationStatus.KeyNotFound, "PDF text did not match the ASIC key pattern — check the excerpt and the pattern in settings.");
                    return;
                }
                row.AsicKey = Truncate(key, 50);
                row.Abn ??= MatchAbn(text);
                if (string.IsNullOrWhiteSpace(row.BusinessName))
                    row.BusinessName = MatchPdfBusinessName(text);
            }

            List<string> contacts;
            try
            {
                contacts = await FindContactsAsync(row);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Auto-retried: credentials or connectivity, not a property of this row.
                Fail(row, AsicKeyNotificationStatus.OntraportUpdateFailed, $"Ontraport lookup failed: {ex.Message}");
                return;
            }

            if (contacts.Count == 0)
            {
                var criteria = string.IsNullOrWhiteSpace(row.BusinessName) ? "" : $" with business name '{row.BusinessName}'";
                if (!string.IsNullOrWhiteSpace(row.Abn)) criteria += $"{(criteria.Length > 0 ? " or" : " with")} ABN {row.Abn}";
                Fail(row, AsicKeyNotificationStatus.ContactNotFound, $"No Ontraport contact{criteria}. Assign one manually.");
                return;
            }

            await WriteKeyAsync(row, contacts, settings);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASIC key notification {Id} ({BusinessName}) failed unexpectedly", row.Id, row.BusinessName);
            // Both of these statuses are auto-retried, which is what an unexpected error deserves.
            var status = string.IsNullOrWhiteSpace(row.AsicKey)
                ? AsicKeyNotificationStatus.DownloadFailed
                : AsicKeyNotificationStatus.OntraportUpdateFailed;
            Fail(row, status, $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<List<string>> FindContactsAsync(AsicKeyNotification row)
    {
        var matches = new List<Dictionary<string, string?>>();
        if (!string.IsNullOrWhiteSpace(row.BusinessName))
            matches = await _ontraport.FindContactsByFieldAsync(FieldBusinessName, row.BusinessName.Trim());

        // Fall back to the ABN, but only accept contacts whose business name agrees — one ABN
        // can hold several business names and the key belongs to exactly one of them.
        if (matches.Count == 0 && !string.IsNullOrWhiteSpace(row.Abn))
        {
            var digits = new string(row.Abn.Where(char.IsDigit).ToArray());
            var byAbn = await _ontraport.FindContactsByFieldAsync(FieldAbn, digits);
            if (byAbn.Count == 0 && digits.Length == 11)
                byAbn = await _ontraport.FindContactsByFieldAsync(FieldAbn, $"{digits[..2]} {digits[2..5]} {digits[5..8]} {digits[8..]}");

            matches = string.IsNullOrWhiteSpace(row.BusinessName)
                ? byAbn
                : byAbn.Where(c => string.Equals(
                        (c.GetValueOrDefault(FieldBusinessName) ?? "").Trim(),
                        row.BusinessName.Trim(),
                        StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return matches
            .Select(c => c.GetValueOrDefault("id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
    }

    private async Task WriteKeyAsync(AsicKeyNotification row, IReadOnlyList<string> contactIds, AsicKeyInboxSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OntraportFieldId))
        {
            // Keep the contact ids so the write goes through on retry once the field is set.
            row.OntraportContactIds = string.Join(",", contactIds);
            Fail(row, AsicKeyNotificationStatus.OntraportUpdateFailed,
                "Ontraport ASIC key field ID is not configured (Settings → ASIC key inbox).");
            return;
        }

        var updated = new List<string>();
        var rejected = new List<string>();
        foreach (var contactId in contactIds)
        {
            var ok = await _ontraport.UpdateContactFieldsAsync(contactId, new Dictionary<string, object>
            {
                ["id"] = contactId,
                [settings.OntraportFieldId.Trim()] = row.AsicKey!,
            });
            (ok ? updated : rejected).Add(contactId);
        }

        row.OntraportContactIds = string.Join(",", updated.Concat(rejected));
        row.OntraportContactsUpdated = updated.Count;
        if (rejected.Count > 0)
        {
            Fail(row, AsicKeyNotificationStatus.OntraportUpdateFailed,
                $"Ontraport rejected the update for contact(s) {string.Join(", ", rejected)}; see server logs.");
            return;
        }

        row.Status = AsicKeyNotificationStatus.Completed;
        row.ErrorMessage = null;
        _logger.LogInformation("ASIC key {AsicKey} for '{BusinessName}' written to Ontraport contact(s) {Contacts}",
            row.AsicKey, row.BusinessName, row.OntraportContactIds);
    }

    private void Fail(AsicKeyNotification row, AsicKeyNotificationStatus status, string error)
    {
        row.Status = status;
        row.ErrorMessage = Truncate(error, 1000);
        _logger.LogWarning("ASIC key notification {Id} ('{BusinessName}') → {Status}: {Error}", row.Id, row.BusinessName, status, error);
    }

    /// <summary>Pulls the download link and the business name out of the email body.</summary>
    private static void ParseEmail(AsicKeyNotification row, MailboxMessage message)
    {
        string? link = null;
        var text = message.TextBody ?? "";

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            // Block-level tags become line breaks so sentences don't run together in TextContent.
            var html = BlockBreakRegex.Replace(message.HtmlBody, "\n");
            var doc = new HtmlParser().ParseDocument(html);

            var anchors = doc.QuerySelectorAll("a[href]")
                .Select(a => (Text: a.TextContent.Trim(), Href: (a.GetAttribute("href") ?? "").Trim()))
                .Where(a => a.Href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // The template's anchor reads "Click here"; any asic.gov.au link is the next best guess.
            link = anchors.FirstOrDefault(a => a.Text.Contains("click here", StringComparison.OrdinalIgnoreCase)).Href
                ?? anchors.FirstOrDefault(a => a.Href.Contains("asic.gov.au", StringComparison.OrdinalIgnoreCase)).Href;

            if (string.IsNullOrWhiteSpace(text))
                text = doc.Body?.TextContent ?? "";
        }

        // Plain-text-only email: the link is the first URL in the body.
        if (string.IsNullOrWhiteSpace(link))
        {
            var urlMatch = UrlRegex.Match(text);
            if (urlMatch.Success) link = urlMatch.Value.TrimEnd('.', ',', ')');
        }

        row.DownloadUrl = string.IsNullOrWhiteSpace(link) ? row.DownloadUrl : Truncate(link, 2000);

        var nameMatch = BusinessNameRegex.Match(text);
        if (nameMatch.Success)
        {
            var name = Regex.Replace(nameMatch.Groups[1].Value, @"\s+", " ").Trim().TrimEnd('.');
            if (name.Length > 0) row.BusinessName = Truncate(name, 200);
        }
    }

    private async Task<(byte[] Bytes, string? Error)> DownloadPdfAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return ([], $"Download returned HTTP {(int)response.StatusCode} — the link may have expired (ASIC keeps them for 30 days).");

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (!LooksLikePdf(bytes))
            {
                var type = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                return ([], $"Download was not a PDF (content-type {type}, {bytes.Length} bytes) — the link may have expired.");
            }
            return (bytes, null);
        }
        catch (HttpRequestException ex)
        {
            return ([], $"Download failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ([], "Download timed out.");
        }
    }

    // "%PDF" normally sits at byte 0, but a few generators prepend junk; scan the first 1 KB.
    private static bool LooksLikePdf(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, 1024);
        return limit >= 4 && bytes.AsSpan(0, limit).IndexOf("%PDF"u8) >= 0;
    }

    private static string ExtractPdfText(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
        return sb.ToString();
    }

    private static string? MatchKey(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("pattern is empty");

        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        var match = regex.Match(text);
        if (!match.Success) return null;

        // Long business names push the key onto the next line after its hyphen ("1-\n7956…"),
        // and keys never legitimately contain whitespace, so collapse it.
        var raw = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        var value = string.Concat(raw.Where(c => !char.IsWhiteSpace(c)));
        return value.Length == 0 ? null : value;
    }

    private static string? MatchAbn(string text)
    {
        var match = AbnRegex.Match(text);
        return match.Success ? new string(match.Groups[1].Value.Where(char.IsDigit).ToArray()) : null;
    }

    private static string? MatchPdfBusinessName(string text)
    {
        var match = PdfBusinessNameRegex.Match(text);
        if (!match.Success) return null;
        var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var name = Regex.Replace(raw, @"\s+", " ").Trim();
        return name.Length == 0 ? null : Truncate(name, 200);
    }

    private static string? Truncate(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}
