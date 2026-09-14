using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>
/// IMAP reader for the ASIC notification inbox (Gmail by default). Deliberately never
/// mutates the mailbox — no flags, no moves — so a human reading the same inbox sees
/// nothing change; de-duplication is by Message-ID on our side.
/// </summary>
public sealed class ImapAsicNotificationMailbox : IAsicNotificationMailbox
{
    private readonly IOptionsMonitor<AsicKeyInboxSettings> _settings;
    private readonly ILogger<ImapAsicNotificationMailbox> _logger;

    public ImapAsicNotificationMailbox(IOptionsMonitor<AsicKeyInboxSettings> settings, ILogger<ImapAsicNotificationMailbox> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MailboxMessage>> FetchNewNotificationsAsync(
        DateTime since, Func<string, bool> isKnown, CancellationToken ct = default)
    {
        var settings = _settings.CurrentValue;
        using var client = await ConnectAsync(settings, ct);
        var folder = await OpenFolderAsync(client, settings, ct);

        // Server-side narrowing: subject substring + date window. IMAP SINCE is date-granular
        // and ignores the time, which is fine — Message-ID de-dup handles the overlap.
        var query = SearchQuery.SubjectContains(settings.SubjectFilter)
            .And(SearchQuery.DeliveredAfter(since.Date));
        var uids = await folder.SearchAsync(query, ct);
        if (uids.Count == 0)
        {
            await client.DisconnectAsync(true, ct);
            return [];
        }

        // Envelope-only fetch first so already-processed messages cost one header, not a body.
        var summaries = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate, ct);
        var results = new List<MailboxMessage>();
        foreach (var summary in summaries)
        {
            ct.ThrowIfCancellationRequested();
            var messageId = NormalizeMessageId(summary.Envelope?.MessageId, summary.UniqueId);
            if (isKnown(messageId))
                continue;

            var message = await folder.GetMessageAsync(summary.UniqueId, ct);
            results.Add(ToMailboxMessage(message, messageId, summary.InternalDate));
        }

        await client.DisconnectAsync(true, ct);
        _logger.LogInformation("ASIC key inbox: {Matched} messages match '{Subject}' since {Since:yyyy-MM-dd}, {New} new",
            uids.Count, settings.SubjectFilter, since, results.Count);
        return results;
    }

    public async Task<MailboxMessage?> FetchByMessageIdAsync(string messageId, CancellationToken ct = default)
    {
        var settings = _settings.CurrentValue;
        using var client = await ConnectAsync(settings, ct);
        var folder = await OpenFolderAsync(client, settings, ct);

        // Message-IDs are stored without angle brackets; the header search wants the bare value too.
        var uids = await folder.SearchAsync(SearchQuery.HeaderContains("Message-ID", messageId.Trim('<', '>')), ct);
        if (uids.Count == 0)
        {
            await client.DisconnectAsync(true, ct);
            return null;
        }

        var summaries = await folder.FetchAsync([uids[0]], MessageSummaryItems.InternalDate, ct);
        var message = await folder.GetMessageAsync(uids[0], ct);
        await client.DisconnectAsync(true, ct);
        return ToMailboxMessage(message, messageId, summaries.FirstOrDefault()?.InternalDate);
    }

    public async Task<MailboxProbeResult> ProbeAsync(AsicKeyInboxSettings settings, CancellationToken ct = default)
    {
        using var client = await ConnectAsync(settings, ct);
        IMailFolder folder;
        try
        {
            folder = await OpenFolderAsync(client, settings, ct);
        }
        catch (FolderNotFoundException ex)
        {
            throw new InvalidOperationException($"Folder '{settings.Folder}' doesn't exist on this mailbox. Gmail labels appear as folders — try INBOX or \"[Gmail]/All Mail\".", ex);
        }

        var since = DateTime.UtcNow.AddDays(-Math.Max(1, settings.LookbackDays));
        var uids = await folder.SearchAsync(
            SearchQuery.SubjectContains(settings.SubjectFilter).And(SearchQuery.DeliveredAfter(since.Date)), ct);

        string? latestSubject = null;
        DateTime? latestReceived = null;
        if (uids.Count > 0)
        {
            // UIDs ascend with arrival, so the last one is the newest match.
            var summary = (await folder.FetchAsync([uids[^1]], MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate, ct)).FirstOrDefault();
            latestSubject = summary?.Envelope?.Subject;
            latestReceived = summary?.InternalDate?.UtcDateTime;
        }

        var result = new MailboxProbeResult(folder.FullName, folder.Count, uids.Count, latestSubject, latestReceived);
        await client.DisconnectAsync(true, ct);
        return result;
    }

    private static async Task<ImapClient> ConnectAsync(AsicKeyInboxSettings settings, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 30_000 };
        try
        {
            await client.ConnectAsync(settings.ImapHost, settings.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await client.AuthenticateAsync(settings.Username.Trim(), NormalizePassword(settings), ct);
            return client;
        }
        catch (AuthenticationException ex)
        {
            client.Dispose();
            // Google's own wording ("Application-specific password required") is accurate but
            // easy to misread as a code problem; spell out what the operator needs to do.
            var hint = ex.Message.Contains("Application-specific password", StringComparison.OrdinalIgnoreCase)
                ? " Google rejects the account password for IMAP — create an app password (Google Account → Security → 2-Step Verification → App passwords) and paste that into Settings → ASIC key inbox."
                : " Check the address and app password in Settings → ASIC key inbox.";
            throw new InvalidOperationException($"{settings.ImapHost} refused the login: {ex.Message}.{hint}", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // Google shows app passwords as "xxxx xxxx xxxx xxxx"; the spaces are display-only and
    // pasting them verbatim fails auth. Only Gmail gets this treatment — other IMAP hosts may
    // legitimately have spaces in a password.
    private static string NormalizePassword(AsicKeyInboxSettings settings)
    {
        var isGmail = settings.ImapHost.Contains("gmail.com", StringComparison.OrdinalIgnoreCase)
                   || settings.ImapHost.Contains("googlemail.com", StringComparison.OrdinalIgnoreCase);
        return isGmail ? string.Concat(settings.Password.Where(c => !char.IsWhiteSpace(c))) : settings.Password;
    }

    private static async Task<IMailFolder> OpenFolderAsync(ImapClient client, AsicKeyInboxSettings settings, CancellationToken ct)
    {
        var folder = string.IsNullOrWhiteSpace(settings.Folder) || settings.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(settings.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        return folder;
    }

    private static MailboxMessage ToMailboxMessage(MimeMessage message, string messageId, DateTimeOffset? internalDate)
    {
        var received = internalDate?.UtcDateTime
            ?? (message.Date == default ? DateTime.UtcNow : message.Date.UtcDateTime);
        return new MailboxMessage(
            messageId,
            message.Subject ?? "",
            message.From?.ToString() ?? "",
            received,
            message.HtmlBody,
            message.TextBody);
    }

    // A message without a Message-ID header is rare but legal; fall back to a folder-scoped
    // synthetic id so de-dup still works within this mailbox.
    private static string NormalizeMessageId(string? envelopeId, UniqueId uid) =>
        string.IsNullOrWhiteSpace(envelopeId) ? $"uid:{uid.Validity}:{uid.Id}" : envelopeId.Trim().Trim('<', '>');
}
