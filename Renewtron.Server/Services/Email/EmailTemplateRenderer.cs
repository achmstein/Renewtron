using System.Text.Encodings.Web;
using System.Text.Unicode;
using Fluid;
using Fluid.Values;

namespace Renewtron.Services.Email;

/// <summary>A template's wording: subject, heading and body. Version 0 is the wording in code.</summary>
public sealed record TemplateContent(string Subject, string Heading, string Body, int Version = 0);

/// <summary>Something wrong with a template: which part ("subject", "heading", "body", "layout") and what.</summary>
public sealed record TemplateProblem(string Field, string Message);

/// <summary>An email ready to send, or the problems that stopped it.</summary>
public sealed record RenderedEmail(string Subject, string Html, string Text, IReadOnlyList<TemplateProblem> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// Turns a template and its values into an email: Liquid for the subject, heading and body (values in
/// the body are HTML-encoded, so a business name can't add markup), the body placed in the layout, the
/// layout's styles copied onto each element, and the plain-text part made from the result.
/// </summary>
public static class EmailTemplateRenderer
{
    private static readonly FluidParser Parser = new();

    // Encodes &, <, >, quotes; leaves letters like é and ✓ readable.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static async Task<RenderedEmail> RenderAsync(
        TemplateContent content, string layout, IReadOnlyDictionary<string, object?> model)
    {
        var problems = new List<TemplateProblem>();
        var subject = await RenderPartAsync("subject", content.Subject, model, NullEncoder.Default, problems);
        var heading = await RenderPartAsync("heading", content.Heading, model, NullEncoder.Default, problems);
        var body = await RenderPartAsync("body", content.Body, model, Html, problems);
        subject = EmailText.SubjectSafe(subject);

        var layoutModel = new Dictionary<string, object?>(model)
        {
            ["subject"] = subject,
            ["heading"] = heading.Trim(),
            ["year"] = DateTime.UtcNow.Year.ToString(),
            ["content"] = new StringValue(body, encode: false),
        };
        var page = await RenderPartAsync("layout", layout, layoutModel, Html, problems);

        if (problems.Count > 0) return new RenderedEmail(subject, "", "", problems);

        var text = HtmlToText.Convert(page);
        var inlined = PreMailer.Net.PreMailer.MoveCssInline(page).Html;
        return new RenderedEmail(subject, inlined, text, problems);
    }

    /// <summary>
    /// Everything that would stop a template going out: Liquid that doesn't parse, an unknown filter, a
    /// variable this email doesn't have (a typo like {{ busines_name }}), and a layout without {{ content }}.
    /// Renders with the sample values, so it catches problems inside "show only if" sections too only when
    /// the sample takes that branch; parsing catches the rest.
    /// </summary>
    public static async Task<IReadOnlyList<TemplateProblem>> CheckAsync(
        EmailTemplateDefinition definition, TemplateContent content, string layout)
    {
        var model = EmailTemplateCatalog.SampleModel(definition);
        var problems = new List<TemplateProblem>();
        foreach (var (field, source) in new[] { ("subject", content.Subject), ("heading", content.Heading), ("body", content.Body) })
            CheckParses(field, source, problems);
        if (problems.Count > 0) return problems;

        // Each "show only if" flag both ways, so a typo in either branch is caught.
        foreach (var variant in FlagVariants(definition, model))
        {
            var rendered = await RenderAsync(content, layout, variant);
            foreach (var p in rendered.Problems.Where(p => !problems.Contains(p))) problems.Add(p);
        }
        return problems;
    }

    /// <summary>Problems with the layout itself: it must parse and contain {{ content }}.</summary>
    public static async Task<IReadOnlyList<TemplateProblem>> CheckLayoutAsync(string layout)
    {
        var problems = new List<TemplateProblem>();
        if (!Parser.TryParse(layout, out _, out var error))
        {
            problems.Add(new("layout", Friendly(error)));
            return problems;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(layout, @"\{\{-?\s*content\s*(\|[^}]*)?-?\}\}"))
            problems.Add(new("layout", "The layout must include {{ content }} where each email's message goes."));

        var sample = EmailTemplateCatalog.All.First(d => d.DefaultBody.Length > 0);
        var rendered = await RenderAsync(new TemplateContent(sample.DefaultSubject, sample.DefaultHeading, sample.DefaultBody),
            layout, EmailTemplateCatalog.SampleModel(sample));
        problems.AddRange(rendered.Problems.Where(p => p.Field == "layout"));
        return problems;
    }

    private static void CheckParses(string field, string source, List<TemplateProblem> problems)
    {
        if (!Parser.TryParse(source, out _, out var error))
            problems.Add(new(field, Friendly(error)));
    }

    private static IEnumerable<Dictionary<string, object?>> FlagVariants(EmailTemplateDefinition d, Dictionary<string, object?> sample)
    {
        yield return sample;
        var flags = d.Variables.Where(v => v.Kind == TemplateVariableKind.Flag).ToList();
        if (flags.Count == 0) yield break;
        yield return flags.Aggregate(new Dictionary<string, object?>(sample), (m, f) => { m[f.Name] = !(bool)f.Sample; return m; });
    }

    private static async Task<string> RenderPartAsync(
        string field, string source, IReadOnlyDictionary<string, object?> model, TextEncoder encoder, List<TemplateProblem> problems)
    {
        if (string.IsNullOrEmpty(source)) return "";
        if (!Parser.TryParse(source, out var template, out var error))
        {
            problems.Add(new(field, Friendly(error)));
            return "";
        }

        var unknown = new List<string>();
        var options = new TemplateOptions
        {
            StrictFilters = true,
            MaxSteps = 100_000,
            Undefined = name =>
            {
                unknown.Add(name);
                return ValueTask.FromResult<FluidValue>(NilValue.Instance);
            },
        };
        var context = new TemplateContext(options);
        foreach (var (name, value) in model)
            context.SetValue(name, value switch
            {
                null => StringValue.Empty,
                FluidValue fv => fv,
                _ => FluidValue.Create(value, options),
            });

        try
        {
            var output = await template.RenderAsync(context, encoder);
            foreach (var name in unknown.Distinct())
                problems.Add(new(field, $"\"{name}\" isn't something this email knows. Check the spelling, or pick it from Insert → Variable."));
            return output;
        }
        catch (Exception ex) when (ex is FluidException or InvalidOperationException or ArgumentException)
        {
            problems.Add(new(field, Friendly(ex.Message)));
            return "";
        }
    }

    private static string Friendly(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "This doesn't read as a template.";
        var e = error.Trim();
        if (e.Contains("Undefined filter", StringComparison.OrdinalIgnoreCase)) return e.Replace("Undefined filter", "Unknown filter");
        return e;
    }
}
