namespace Renewtron.Services.Email;

/// <summary>
/// The page every email goes inside: brand colours, the heading band, the footer, and the styles for the
/// blocks templates use (button, details table, callouts). Stored like a template under
/// <see cref="EmailTemplateCatalog.Layout"/> (its Body), so it has versions too; edited only in code view.
/// Styles are copied onto each element before sending, because some email apps drop &lt;style&gt;.
/// </summary>
public static class EmailLayout
{
    /// <summary>What the layout can use besides the email's own variables.</summary>
    public static readonly IReadOnlyList<TemplateVariable> Variables =
    [
        new("content", "The email's message", "The template's body. Must appear once: {{ content }}.", TemplateVariableKind.Text, "<p>Hello.</p>"),
        new("heading", "Heading", "The template's heading, empty when it has none.", TemplateVariableKind.Text, "Renewal Successful"),
        new("subject", "Subject", "The email's subject line.", TemplateVariableKind.Text, "Business Name Renewal Confirmation"),
        new("year", "Year", "This year, for a copyright line.", TemplateVariableKind.Text, "2026"),
    ];

    public const string Default = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{ subject }}</title>
        <style>
          body { margin: 0; padding: 0; background-color: #f3f4f6; font-family: Arial, Helvetica, sans-serif; line-height: 1.6; color: #374151; }
          .wrapper { background-color: #f3f4f6; padding: 24px 12px; }
          .container { max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; }
          .header { background-color: #4F46E5; color: #ffffff; padding: 28px 24px; text-align: center; }
          .header h1 { margin: 0; font-size: 24px; font-weight: bold; color: #ffffff; }
          .content { padding: 28px 24px; font-size: 15px; }
          .content p { margin: 0 0 14px 0; }
          .content ul, .content ol { margin: 0 0 14px 0; padding-left: 22px; }
          .content li p { margin: 0 0 4px 0; }
          .content a { color: #4F46E5; }
          .content h2 { font-size: 18px; margin: 20px 0 10px 0; color: #111827; }
          .content h3 { font-size: 16px; margin: 18px 0 8px 0; color: #111827; }
          .details { width: 100%; border-collapse: collapse; margin: 0 0 16px 0; background-color: #f9fafb; border: 1px solid #e5e7eb; }
          .details th { text-align: left; vertical-align: top; padding: 9px 12px; color: #6B7280; font-weight: bold; font-size: 14px; border-bottom: 1px solid #e5e7eb; width: 40%; }
          .details td { padding: 9px 12px; color: #111827; border-bottom: 1px solid #e5e7eb; }
          .button-row { text-align: center; margin: 24px 0 14px 0; }
          a.button { background-color: #4F46E5; color: #ffffff; padding: 12px 22px; border-radius: 6px; text-decoration: none; display: inline-block; font-weight: bold; }
          .callout { padding: 12px 16px; margin: 0 0 16px 0; border-left: 4px solid #4F46E5; background-color: #eef2ff; }
          .callout p { margin: 0 0 6px 0; }
          .callout-info { border-left-color: #2563eb; background-color: #dbeafe; }
          .callout-warning { border-left-color: #f59e0b; background-color: #fef3c7; }
          .callout-success { border-left-color: #10B981; background-color: #d1fae5; }
          .callout-note { border-left-color: #d1d5db; background-color: #f9fafb; color: #6B7280; font-size: 13px; }
          .footer { background-color: #f9fafb; padding: 20px 24px; text-align: center; font-size: 12px; color: #6B7280; border-top: 1px solid #e5e7eb; }
          .footer p { margin: 0 0 6px 0; }
        </style>
        </head>
        <body>
        <div class="wrapper">
        <div class="container">
        {% if heading != empty %}<div class="header"><h1>{{ heading }}</h1></div>{% endif %}
        <div class="content">
        {{ content }}
        </div>
        <div class="footer">
        <p>This is an automated email from Renewtron. Please do not reply to this email.</p>
        <p>If you have any questions, please contact our support team.</p>
        </div>
        </div>
        </div>
        </body>
        </html>
        """;
}
