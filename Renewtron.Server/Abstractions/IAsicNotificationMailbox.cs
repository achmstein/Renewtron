using Renewtron.Settings;

namespace Renewtron.Abstractions;

/// <summary>A fetched email, reduced to what the ASIC key pipeline needs.</summary>
public sealed record MailboxMessage(
    string MessageId,
    string Subject,
    string From,
    DateTime ReceivedAt,
    string? HtmlBody,
    string? TextBody);

/// <summary>What a "Test connection" from the settings page reports back.</summary>
public sealed record MailboxProbeResult(
    string Folder,
    int MessagesInFolder,
    int MatchingInLookback,
    string? LatestSubject,
    DateTime? LatestReceivedAt);

/// <summary>
/// Read-only view of the inbox that receives ASIC's "Notification request" emails.
/// Abstracted so the IMAP implementation can be swapped for the Gmail API without
/// touching the pipeline.
/// </summary>
public interface IAsicNotificationMailbox
{
    /// <summary>
    /// Returns matching messages received since <paramref name="since"/> whose Message-ID
    /// isn't already known. Full bodies are only fetched for the unknown ones.
    /// </summary>
    Task<IReadOnlyList<MailboxMessage>> FetchNewNotificationsAsync(
        DateTime since,
        Func<string, bool> isKnown,
        CancellationToken ct = default);

    /// <summary>Re-fetches a single message by Message-ID (used when a row is retried).</summary>
    Task<MailboxMessage?> FetchByMessageIdAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Connects with the given (possibly unsaved) settings, opens the folder and runs the
    /// subject search — the settings page's "Test connection". Throws with an operator-readable
    /// message on failure.
    /// </summary>
    Task<MailboxProbeResult> ProbeAsync(AsicKeyInboxSettings settings, CancellationToken ct = default);
}
