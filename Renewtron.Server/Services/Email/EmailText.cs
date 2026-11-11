using System.Net;

namespace Renewtron.Services;

/// <summary>Encoding helpers for customer emails. Business names, contact names and ASIC
/// references come from customers and third parties, so they're HTML-encoded before they
/// reach markup, and CR/LF is stripped before a value is used in a subject line.</summary>
public static class EmailText
{
    public static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string SubjectSafe(string? value) =>
        (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}
