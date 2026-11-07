using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renewtron.Data;

namespace Renewtron.Services;

/// <summary>
/// SendGrid's Event Webhook: what happened to each email after SendGrid took it (delivered, bounced,
/// dropped, opened…), and whether email is working at all. Pure rules are static and unit tested.
/// </summary>
public static class EmailEvents
{
    /// <summary>
    /// SendGrid's signed events: an ECDSA P-256 signature (base64, DER) over timestamp + raw body,
    /// checked with the account's verification key (base64 SubjectPublicKeyInfo).
    /// </summary>
    public static bool VerifySignature(string publicKeyBase64, string signatureBase64, string timestamp, string body)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
            return ecdsa.VerifyData(Encoding.UTF8.GetBytes(timestamp + body), Convert.FromBase64String(signatureBase64.Trim()),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { return false; }
    }

    public sealed record Event(string Kind, DateTime At, string? MessageId, Guid? OutboundId, string? Detail);

    /// <summary>The events in a webhook body (a JSON array). Unknown shapes are skipped.</summary>
    public static IReadOnlyList<Event> Parse(string body)
    {
        var events = new List<Event>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return events;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string? Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var kind = Str("event");
            if (kind is null) continue;
            var at = e.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var secs)
                ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime : DateTime.UtcNow;
            // sg_message_id is "<X-Message-Id>.<filter info>"; the part before the first dot is ours.
            var sg = Str("sg_message_id");
            var messageId = sg is null ? null : sg.Split('.')[0];
            Guid? outbound = Guid.TryParse(Str("outbound_id"), out var g) ? g : null;
            var detail = Str("reason") ?? Str("response") ?? Str("type");
            events.Add(new Event(kind.ToLowerInvariant(), at, messageId, outbound, detail));
        }
        return events;
    }

    /// <summary>How final each delivery event is: a later event only replaces one that isn't more final.</summary>
    public static int Rank(string? kind) => kind switch
    {
        "bounce" or "dropped" or "spamreport" => 3,
        "delivered" => 2,
        "deferred" => 1,
        _ => 0,
    };

    /// <summary>Applies one event to its row: delivery status (never stepping back), opened, and the reason.</summary>
    public static void Apply(OutboundMessage m, Event e)
    {
        if (e.Kind == "open") { m.OpenedAt ??= e.At; return; }
        if (Rank(e.Kind) == 0) return; // processed, click, unsubscribe…
        if (Rank(e.Kind) < Rank(m.DeliveryStatus)) return;
        m.DeliveryStatus = e.Kind;
        m.DeliveryAt = e.At;
        if (e.Detail is { Length: > 0 } d) m.DeliveryDetail = d.Length > 500 ? d[..500] : d;
        else if (Rank(e.Kind) == 2) m.DeliveryDetail = null;
    }

    /// <summary>
    /// Is email working? A problem when the last 3 sends all failed, or a send failed with SendGrid
    /// refusing our key (401/403) in the last 24 hours: every email is failing then.
    /// </summary>
    public static async Task<string?> ProblemAsync(ApplicationDbContext db, DateTime now)
    {
        var last = await db.OutboundMessages.AsNoTracking()
            .Where(m => m.Channel == OutboundChannel.Email && m.Status != OutboundStatus.Queued)
            .OrderByDescending(m => m.LastAttemptAt ?? m.CreatedAt).Take(3)
            .Select(m => new { m.Status, m.ErrorMessage, At = m.LastAttemptAt ?? m.CreatedAt })
            .ToListAsync();
        return Problem(last.Select(l => (l.Status, l.ErrorMessage, l.At)).ToList(), now);
    }

    /// <summary>The rule behind <see cref="ProblemAsync"/>, given the latest sends (newest first).</summary>
    public static string? Problem(IReadOnlyList<(OutboundStatus Status, string? Error, DateTime At)> latest, DateTime now)
    {
        if (latest.Count == 0 || latest[0].Status != OutboundStatus.Failed) return null;
        var newest = latest[0];
        var keyRefused = newest.Error is { } e && (e.Contains("401") || e.Contains("403") || e.Contains("not configured", StringComparison.OrdinalIgnoreCase));
        if (keyRefused && now - newest.At < TimeSpan.FromHours(24))
            return $"SendGrid is refusing our API key, so no email is going out ({newest.Error}). Put a new key in Settings → SendGrid.";
        if (latest.Count >= 3 && latest.All(l => l.Status == OutboundStatus.Failed))
            return $"The last {latest.Count} emails failed ({newest.Error}). See Emails.";
        return null;
    }

    /// <summary>Daily: clears the stored text of emails older than 180 days (the record stays).</summary>
    public static async Task<int> PurgeOldBodiesAsync(ApplicationDbContext db)
    {
        var before = DateTime.UtcNow.AddDays(-180);
        return await db.OutboundMessages.Where(m => m.CreatedAt < before && (m.BodyHtml != null || m.BodyText != null))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.BodyHtml, (string?)null).SetProperty(m => m.BodyText, (string?)null));
    }
}

/// <summary>Hangfire entry point for the daily body purge.</summary>
public sealed class EmailMaintenance(ApplicationDbContext db)
{
    public Task<int> PurgeOldBodiesAsync() => EmailEvents.PurgeOldBodiesAsync(db);
}
