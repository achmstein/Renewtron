using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Renewtron.Services.Email;

/// <summary>
/// The plain-text part of an email, made from its HTML so the two always say the same thing. Paragraphs
/// and headings become lines with a blank line between, lists become "- " lines, a details table becomes
/// "Label: value" lines, a button becomes "Label: link", and the footer is set off with "---".
/// </summary>
public static partial class HtmlToText
{
    private static readonly HtmlParser Parser = new();

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "head", "style", "script", "title", "template" };
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "table", "blockquote", "section", "header", "footer", "center",
    };

    public static string Convert(string html)
    {
        var doc = Parser.ParseDocument(html);
        var sb = new StringBuilder();
        Write(doc.Body ?? (INode)doc.DocumentElement, sb);
        return Tidy(sb.ToString());
    }

    private static void Write(INode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                // Whitespace between list items or table rows would split them with blank lines.
                case IText text when string.IsNullOrWhiteSpace(text.Data) && node is IElement { LocalName: "ul" or "ol" or "table" or "tbody" or "thead" or "tr" }:
                    break;
                case IText text:
                    sb.Append(Whitespace().Replace(text.Data, " "));
                    break;
                case IElement el:
                    WriteElement(el, sb);
                    break;
            }
        }
    }

    private static void WriteElement(IElement el, StringBuilder sb)
    {
        var tag = el.LocalName;
        if (Skip.Contains(tag)) return;

        switch (tag)
        {
            case "br":
                sb.Append('\n');
                return;
            case "hr":
                sb.Append("\n\n---\n\n");
                return;
            case "li":
                sb.Append('\n').Append("- ").Append(Inline(el));
                return;
            case "tr":
                var cells = el.Children.Where(c => c.LocalName is "th" or "td").Select(Inline).Where(c => c.Length > 0).ToList();
                if (cells.Count == 2 && el.Children.First().LocalName == "th")
                    sb.Append('\n').Append(cells[0]).Append(": ").Append(cells[1]);
                else if (cells.Count > 0)
                    sb.Append('\n').Append(string.Join("  ", cells));
                return;
            case "a":
                var label = Inline(el);
                var href = el.GetAttribute("href")?.Trim() ?? "";
                var isLink = href.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                if (!isLink || string.Equals(label, href, StringComparison.OrdinalIgnoreCase) || label.Length == 0)
                    sb.Append(label.Length > 0 ? label : href);
                else if (el.ClassList.Contains("button"))
                    sb.Append(label).Append(": ").Append(href);
                else
                    sb.Append(label).Append(" (").Append(href).Append(')');
                return;
        }

        var block = Blocks.Contains(tag);
        if (block) sb.Append("\n\n");
        if (el.ClassList.Contains("footer")) sb.Append("---\n");
        Write(el, sb);
        if (block) sb.Append("\n\n");
    }

    /// <summary>An element's text on one line (for a list item or table cell).</summary>
    private static string Inline(IElement el)
    {
        var sb = new StringBuilder();
        Write(el, sb);
        var lines = sb.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join(" ", lines);
    }

    private static string Tidy(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim());
        var joined = string.Join("\n", lines);
        return BlankRuns().Replace(joined, "\n\n").Trim() + "\n";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();
}
