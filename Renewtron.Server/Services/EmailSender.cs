using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Renewtron.Services;

/// <summary>An email to send, and what it's about (for the Emails page and the client's record).</summary>
public sealed record EmailToSend(
    string Kind,
    string To,
    string Subject,
    string PlainText,
    string Html,
    Guid? LeadId = null,
    Guid? ClientId = null,
    Guid? RenewalRequestId = null,
    Guid? BatchId = null,
    /// <summary>An OutboundMessage row already written for this send (win-back writes its own up front).</summary>
    Guid? MessageId = null,
    Guid? ResendOfId = null);

/// <summary>
/// Every email Renewtron sends goes through here. It writes an <see cref="OutboundMessage"/> first, sends
/// through SendGrid, then marks the row Sent (with SendGrid's message id) or Failed (with SendGrid's
/// answer). A failure still throws, so callers and Hangfire retries behave as before; a retry of the same
/// email updates its row instead of adding one.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends and records. Returns the row's id; throws when SendGrid didn't take it.</summary>
    Task<Guid> SendAsync(EmailToSend email, CancellationToken ct = default);
}

public sealed class EmailSender(
    ApplicationDbContext db,
    IOptionsSnapshot<SendGridSettings> settings,
    ILogger<EmailSender> logger) : IEmailSender
{
    /// <summary>A failed send of the same email within this long is the same email, retried.</summary>
    private static readonly TimeSpan RetryWindow = TimeSpan.FromDays(1);

    public async Task<Guid> SendAsync(EmailToSend email, CancellationToken ct = default)
    {
        var row = await RowForAsync(email, ct);
        row.AttemptCount++;
        row.LastAttemptAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var s = settings.Value;
        try
        {
            if (string.IsNullOrWhiteSpace(s.ApiKey))
                throw new InvalidOperationException("SendGrid API key is not configured.");

            var client = new SendGridClient(s.ApiKey);
            var msg = MailHelper.CreateSingleEmail(new EmailAddress(s.FromEmail, s.FromName), new EmailAddress(email.To),
                email.Subject, email.PlainText, email.Html);
            // Carried back on SendGrid's events: the webhook finds the row by it even without the message id.
            msg.AddCustomArg("outbound_id", row.Id.ToString());
            var response = await client.SendEmailAsync(msg, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Body.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"SendGrid returned {(int)response.StatusCode}: {SendGridError(body)}");
            }

            row.Status = OutboundStatus.Sent;
            row.SentAt = DateTime.UtcNow;
            row.ErrorMessage = null;
            row.ProviderMessageId = response.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null;
            await db.SaveChangesAsync(CancellationToken.None);
            return row.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            row.Status = OutboundStatus.Failed;
            row.ErrorMessage = Cap(ex.Message, 1000);
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError("Email not sent ({Kind} to @{Domain}): {Error}", email.Kind, Domain(email.To), ex.Message);
            throw ex is InvalidOperationException ? ex : new InvalidOperationException(ex.Message, ex);
        }
    }

    /// <summary>The row for this send: the one passed in, a recent failed try of the same email, or a new one.</summary>
    private async Task<OutboundMessage> RowForAsync(EmailToSend email, CancellationToken ct)
    {
        if (email.MessageId is { } id && await db.OutboundMessages.FirstOrDefaultAsync(m => m.Id == id, ct) is { } given)
        {
            given.BodyHtml ??= email.Html;
            given.BodyText ??= email.PlainText;
            given.RenewalRequestId ??= email.RenewalRequestId;
            given.ClientId ??= email.ClientId;
            return given;
        }

        var since = DateTime.UtcNow - RetryWindow;
        var retry = email.ResendOfId is null
            ? await db.OutboundMessages
                .Where(m => m.Status == OutboundStatus.Failed && m.CreatedAt >= since
                            && m.Template == email.Kind && m.Recipient == email.To && m.Subject == email.Subject
                            && m.LeadId == email.LeadId && m.RenewalRequestId == email.RenewalRequestId)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct)
            : null;
        if (retry is not null) return retry;

        var row = new OutboundMessage
        {
            Id = Guid.NewGuid(),
            LeadId = email.LeadId,
            ClientId = email.ClientId,
            RenewalRequestId = email.RenewalRequestId,
            BatchId = email.BatchId,
            ResendOfId = email.ResendOfId,
            Channel = OutboundChannel.Email,
            Template = Cap(email.Kind, 64),
            Subject = Cap(email.Subject, 300),
            Recipient = Cap(email.To.Trim(), 300),
            BodyHtml = email.Html,
            BodyText = email.PlainText,
            Status = OutboundStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        db.OutboundMessages.Add(row);
        return row;
    }

    /// <summary>SendGrid's own words from its error JSON, else the raw body (capped).</summary>
    public static string SendGridError(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m));
                var joined = string.Join("; ", messages);
                if (joined.Length > 0) return Cap(joined, 500);
            }
        }
        catch (System.Text.Json.JsonException) { }
        return Cap(body, 500);
    }

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;

    // The domain is enough to diagnose delivery; the full address stays out of the logs.
    private static string Domain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 ? email[(at + 1)..] : "unknown";
    }
}
