using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;

namespace Renewtron.Services.Email;

/// <summary>An email made from its template, ready for <see cref="IEmailSender"/>; null Html when it's switched off.</summary>
public sealed record TemplatedEmail(string Key, int Version, bool Enabled, string Subject, string Html, string Text);

/// <summary>
/// The editable emails: what each one currently says (the latest saved version, or the wording in code),
/// whether it's on, and rendering it for a send. Saving, reset and rollback each add a version.
/// </summary>
public interface IEmailTemplates
{
    /// <summary>The email for these values. When the saved version can't be rendered, falls back to the default and logs it.</summary>
    Task<TemplatedEmail> RenderAsync(string key, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default);
    Task<TemplateContent> CurrentAsync(string key, CancellationToken ct = default);
    Task<string> LayoutAsync(CancellationToken ct = default);
    Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);
    TemplateContent DefaultFor(string key);
    Task<int> SaveAsync(string key, TemplateContent content, string? by, string? note,
        EmailTemplateVersionSource source = EmailTemplateVersionSource.Edit, int? restoredFrom = null, CancellationToken ct = default);
    Task SetEnabledAsync(string key, bool enabled, string? by, CancellationToken ct = default);
}

public sealed class EmailTemplates(
    ApplicationDbContext db,
    IOptionsMonitor<WinBackSettings> winBack,
    ILogger<EmailTemplates> logger) : IEmailTemplates
{
    public async Task<TemplatedEmail> RenderAsync(string key, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(key, ct))
            return new TemplatedEmail(key, 0, false, "", "", "");

        var content = await CurrentAsync(key, ct);
        var layout = await LayoutAsync(ct);
        var rendered = await EmailTemplateRenderer.RenderAsync(content, layout, model);
        if (rendered.Ok)
            return new TemplatedEmail(key, content.Version, true, rendered.Subject, rendered.Html, rendered.Text);

        // A saved version was checked when it was saved, so this is rare (e.g. a variable removed from
        // code since). The customer still gets their email, in the default wording and layout.
        logger.LogError("Email template {Key} v{Version} didn't render, sending the default: {Problems}",
            key, content.Version, string.Join("; ", rendered.Problems.Select(p => $"{p.Field}: {p.Message}")));
        var fallback = await EmailTemplateRenderer.RenderAsync(DefaultFor(key), EmailLayout.Default, model);
        if (!fallback.Ok)
            throw new InvalidOperationException($"Email template {key} didn't render: {fallback.Problems[0].Message}");
        return new TemplatedEmail(key, 0, true, fallback.Subject, fallback.Html, fallback.Text);
    }

    public async Task<TemplateContent> CurrentAsync(string key, CancellationToken ct = default)
    {
        var latest = await db.EmailTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateKey == key)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);
        return latest is null ? DefaultFor(key) : new TemplateContent(latest.Subject, latest.Heading, latest.Body, latest.Version);
    }

    public async Task<string> LayoutAsync(CancellationToken ct = default) =>
        (await CurrentAsync(EmailTemplateCatalog.Layout, ct)).Body;

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct = default) =>
        await db.EmailTemplates.AsNoTracking().Where(t => t.Key == key).Select(t => (bool?)t.Enabled).FirstOrDefaultAsync(ct) ?? true;

    public TemplateContent DefaultFor(string key)
    {
        if (key == EmailTemplateCatalog.Layout) return new TemplateContent("", "", EmailLayout.Default);
        if (key == EmailTemplateCatalog.WinBack) return WinBackDefaults.From(winBack.CurrentValue);
        var d = EmailTemplateCatalog.Find(key) ?? throw new ArgumentException($"No email template \"{key}\".", nameof(key));
        return new TemplateContent(d.DefaultSubject, d.DefaultHeading, Dedent(d.DefaultBody));
    }

    public async Task<int> SaveAsync(string key, TemplateContent content, string? by, string? note,
        EmailTemplateVersionSource source = EmailTemplateVersionSource.Edit, int? restoredFrom = null, CancellationToken ct = default)
    {
        var last = await db.EmailTemplateVersions.Where(v => v.TemplateKey == key).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var version = new EmailTemplateVersion
        {
            TemplateKey = key,
            Version = last + 1,
            Subject = content.Subject,
            Heading = content.Heading,
            Body = content.Body,
            Source = source,
            RestoredFrom = restoredFrom,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = by,
        };
        db.EmailTemplateVersions.Add(version);
        await TouchAsync(key, by, ct);
        await db.SaveChangesAsync(ct);
        return version.Version;
    }

    public async Task SetEnabledAsync(string key, bool enabled, string? by, CancellationToken ct = default)
    {
        var row = await TouchAsync(key, by, ct);
        row.Enabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    private async Task<EmailTemplate> TouchAsync(string key, string? by, CancellationToken ct)
    {
        var row = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Key == key, ct);
        if (row is null)
        {
            row = new EmailTemplate { Key = key, Enabled = true };
            db.EmailTemplates.Add(row);
        }
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = by;
        return row;
    }

    /// <summary>The catalogue's raw strings are indented in code; stored bodies aren't.</summary>
    internal static string Dedent(string body) =>
        string.Join("\n", body.Replace("\r", "").Split('\n').Select(l => l.Trim())).Trim();
}

/// <summary>
/// Win-back's wording was in Settings (with {{FullName}}-style tags) before templates. Until someone saves
/// a version, its default is that wording, converted, so whatever was customised there carries over.
/// </summary>
public static partial class WinBackDefaults
{
    public static TemplateContent From(WinBackSettings s)
    {
        var subject = ConvertTags(s.Subject);
        string body;
        if (!string.IsNullOrWhiteSpace(s.BodyHtml))
        {
            var html = s.BodyHtml;
            // A whole page pasted in: keep what's inside <body>, the layout provides the rest.
            var match = BodyInner().Match(html);
            body = ConvertTags(match.Success ? match.Groups[1].Value.Trim() : html);
        }
        else
        {
            body = PlainToHtml(ConvertTags(s.BodyPlain));
        }
        return new TemplateContent(subject, "", body);
    }

    private static string ConvertTags(string? source) =>
        OldTag().Replace(source ?? "", m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "fullname" => "{{ full_name }}",
            "abn" => "{{ abn }}",
            "email" => "{{ email }}",
            "businessname" => "{{ business_name }}",
            _ => m.Value,
        });

    /// <summary>Blank-line-separated paragraphs, single newlines as line breaks, bare links made clickable.</summary>
    private static string PlainToHtml(string plain)
    {
        var paragraphs = Regex.Split(plain.Replace("\r", "").Trim(), @"\n\s*\n")
            .Where(p => p.Trim().Length > 0)
            .Select(p =>
            {
                var encoded = string.Join("<br>", p.Trim().Split('\n').Select(l => WebUtility.HtmlEncode(l.Trim())));
                // Liquid tags were encoded too ({{ a }} has no HTML specials, so they survive as-is).
                encoded = Link().Replace(encoded, m => $"<a href=\"{m.Value}\">{m.Value}</a>");
                return $"<p>{encoded}</p>";
            });
        return string.Join("\n", paragraphs);
    }

    [GeneratedRegex(@"\{\{\s*(FullName|Abn|Email|BusinessName)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex OldTag();

    [GeneratedRegex(@"<body[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyInner();

    [GeneratedRegex(@"https?://[^\s<]+[^\s<.,;:!?)]")]
    private static partial Regex Link();
}
