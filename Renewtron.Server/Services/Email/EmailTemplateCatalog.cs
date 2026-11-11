namespace Renewtron.Services.Email;

/// <summary>What a variable holds: a piece of text, a yes/no for "show only if", or a list to repeat.</summary>
public enum TemplateVariableKind { Text, Flag, List }

/// <summary>A value an email can use, e.g. <c>{{ business_name }}</c>.</summary>
/// <param name="Sample">Shown in previews and used to check a template before it's saved.</param>
public sealed record TemplateVariable(string Name, string Label, string Description, TemplateVariableKind Kind, object Sample);

/// <summary>An email whose wording lives in a template, and the wording it starts with.</summary>
public sealed record EmailTemplateDefinition(
    string Key,
    string Name,
    string Description,
    /// <summary>What a preview with real data picks from: "renewal" or "lead".</summary>
    string PreviewSource,
    IReadOnlyList<TemplateVariable> Variables,
    string DefaultSubject,
    string DefaultHeading,
    string DefaultBody);

/// <summary>
/// Every email Renewtron writes from a template. Keys are the names the email log already uses. The
/// defaults are the wording that was in code before templates; an admin's saved version replaces one,
/// and "reset to default" brings it back.
/// </summary>
public static class EmailTemplateCatalog
{
    public const string RenewalConfirmation = "renewal confirmation";
    public const string PaidEarly = "paid early";
    public const string LeadCaptured = "lead captured";
    public const string NotDue = "not due for renewal";
    public const string NoBusinessNames = "no business names";
    public const string RenewalInProgress = "renewal in progress";
    public const string WinBack = "win-back";
    public const string PaymentNotFinished = "payment not finished";
    public const string RegistrationReceived = "registration received";
    public const string RegistrationLodged = "registration lodged";
    public const string RegistrationRefused = "registration refused";

    /// <summary>The shared layout every email goes inside. Not a template of its own; see <see cref="EmailLayout"/>.</summary>
    public const string Layout = "layout";

    private static readonly TemplateVariable FullName = new("full_name", "Customer's name", "The name they gave on the renewal site.", TemplateVariableKind.Text, "Jane Citizen");
    private static readonly TemplateVariable Abn = new("abn", "ABN", "Spaced like 12 345 678 901.", TemplateVariableKind.Text, "51 824 753 556");
    private static readonly TemplateVariable Email = new("email", "Customer's email", "The address it's sent to.", TemplateVariableKind.Text, "jane@example.com");


    private static readonly TemplateVariable[] RegistrationVariables =
    [
        FullName,
        new("business_name", "Business name", "The name they asked to register.", TemplateVariableKind.Text, "Citizen Plumbing"),
        Abn,
        new("registration_period", "Registration period", "\"1 Year\" or \"3 Years\".", TemplateVariableKind.Text, "1 Year"),
        new("amount_paid", "Amount paid", "With the dollar sign, e.g. $147.00.", TemplateVariableKind.Text, "$147.00"),
        new("transaction_reference", "ASIC reference", "ASIC's transaction reference, once lodged.", TemplateVariableKind.Text, "1-11AQR319"),
        new("reason", "Why ASIC refused", "ASIC's words, when it refused the name or ABN.", TemplateVariableKind.Text, "ASIC says the name isn't available."),
        new("date", "Date", "When it happened (UTC).", TemplateVariableKind.Text, "November 12, 2026"),
        Email,
    ];

    public static readonly IReadOnlyList<EmailTemplateDefinition> All =
    [
        new(RenewalConfirmation, "Renewal confirmation",
            "Sent when ASIC has renewed a business name we paid for.",
            "renewal",
            [
                new("business_name", "Business name", "The name that was renewed.", TemplateVariableKind.Text, "Citizen Plumbing"),
                Abn,
                new("renewal_period", "Renewal period", "\"1 Year\" or \"3 Years\".", TemplateVariableKind.Text, "1 Year"),
                new("amount_paid", "Amount paid", "With the dollar sign, e.g. $97.00.", TemplateVariableKind.Text, "$97.00"),
                new("transaction_reference", "Transaction reference", "ASIC's reference for the renewal.", TemplateVariableKind.Text, "7E1234567"),
                new("date", "Date renewed", "When the renewal went through (UTC).", TemplateVariableKind.Text, "November 09, 2026 at 10:15 AM UTC"),
                new("portal_url", "Business Portal link", "Signs them straight in when a one-time link is available, otherwise opens the sign-in page.", TemplateVariableKind.Text, "https://portal.example.com/sign-in?token=abc"),
                new("login_url", "Portal sign-in page", "The plain sign-in page, for after the one-time link expires.", TemplateVariableKind.Text, "https://portal.example.com/login"),
                new("has_sign_in_link", "Has a one-time sign-in link", "Yes when the portal button signs them straight in.", TemplateVariableKind.Flag, true),
                new("has_login_link", "Has only the sign-in page", "Yes when the portal button opens the sign-in page instead.", TemplateVariableKind.Flag, false),
                Email,
            ],
            "Business Name Renewal Confirmation - {{ business_name }}",
            "✓ Renewal Successful",
            """
            <p>Dear Valued Customer,</p>
            <p>Your business name has been successfully renewed with ASIC.</p>
            <table class="details"><tbody>
            <tr><th>Business Name</th><td>{{ business_name }}</td></tr>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            <tr><th>Renewal Period</th><td>{{ renewal_period }}</td></tr>
            <tr><th>Amount Paid</th><td>{{ amount_paid }}</td></tr>
            <tr><th>Transaction Reference</th><td>{{ transaction_reference }}</td></tr>
            <tr><th>Date</th><td>{{ date }}</td></tr>
            </tbody></table>
            <p><strong>What's Next?</strong><br>Your business name registration has been extended and is now active with ASIC. Please keep this email for your records.</p>
            {% if has_sign_in_link %}
            <div class="button-row"><a class="button" href="{{ portal_url }}">Open your Business Portal</a></div>
            <div class="callout callout-note"><p>This link signs you in and works once, for 72 hours. After that, sign in with your email at {{ login_url }}.</p></div>
            {% endif %}
            {% if has_login_link %}
            <div class="button-row"><a class="button" href="{{ portal_url }}">View your business names in your Business Portal</a></div>
            {% endif %}
            """),

        new(PaidEarly, "Payment received (paid early)",
            "Sent when a customer pays for names ASIC isn't taking yet. We lodge them when ASIC opens the renewal.",
            "renewal",
            [
                new("business_names", "Business names", "Every name they paid for. Use inside \"Repeat for each\".", TemplateVariableKind.List, new[] { "Citizen Plumbing", "Citizen Gas Fitting" }),
                new("first_business_name", "First business name", "The first (often only) name.", TemplateVariableKind.Text, "Citizen Plumbing"),
                new("names_count", "Number of names", "How many names they paid for.", TemplateVariableKind.Text, 2),
                new("is_one_name", "Paid for one name", "Yes when they paid for a single name.", TemplateVariableKind.Flag, false),
                Abn,
                new("amount_paid", "Amount paid", "With the dollar sign, e.g. $194.00.", TemplateVariableKind.Text, "$194.00"),
                new("term", "Term", "\"1 year\" or \"3 years\".", TemplateVariableKind.Text, "1 year"),
                new("your_names", "\"your business name(s)\"", "\"your business name\" or \"your business names\".", TemplateVariableKind.Text, "your business names"),
                new("its", "\"its/their\"", "\"its\" for one name, \"their\" for several.", TemplateVariableKind.Text, "their"),
                new("it_has", "\"it has/they have\"", "\"it has\" for one name, \"they have\" for several.", TemplateVariableKind.Text, "they have"),
                Email,
            ],
            "{% if is_one_name %}Payment received - {{ first_business_name }} renewal{% else %}Payment received - renewal of {{ names_count }} business names{% endif %}",
            "✓ Payment Received",
            """
            <p>Dear Valued Customer,</p>
            <p>Thank you, we've received your payment of <strong>{{ amount_paid }}</strong> to renew {{ your_names }} for {{ term }} (ABN {{ abn }}):</p>
            {% for name in business_names %}
            <p><strong>{{ name }}</strong></p>
            {% endfor %}
            <p><strong>What happens next?</strong><br>ASIC only accepts a business name renewal in the month before it's due, so we'll lodge yours with ASIC as soon as {{ its }} renewal opens. You don't need to do anything, and your business name stays registered in the meantime.</p>
            <p>We'll email you again with ASIC's confirmation once {{ it_has }} been renewed. Please keep this email for your records.</p>
            """),

        new(LeadCaptured, "Thanks for using Renewtron",
            "Sent after someone searches their ABN on the renewal site and leaves their details.",
            "lead",
            [FullName, Abn, new("date", "Date", "The day they searched.", TemplateVariableKind.Text, "November 09, 2026"), Email],
            "Thank You for Using Renewtron",
            "Thank You for Using Renewtron",
            """
            <p>Hi {{ full_name }},</p>
            <p>Thank you for using Renewtron for your business name renewal needs.</p>
            <table class="details"><tbody>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            <tr><th>Date</th><td>{{ date }}</td></tr>
            </tbody></table>
            <p>We've saved your details and will be here whenever you need to renew your business name.</p>
            <p>If you have any questions, please don't hesitate to contact us.</p>
            """),

        new(NotDue, "Registration status (not due yet)",
            "Sent when the ABN they searched has names that aren't due for renewal.",
            "lead",
            [FullName, Abn, new("reminder_opt_in", "Asked for a reminder", "Yes when they ticked \"remind me when it's due\".", TemplateVariableKind.Flag, true), Email],
            "Your Business Name Registration Status",
            "Your Business Name Registration Status",
            """
            <p>Hi {{ full_name }},</p>
            <p>Thank you for checking your business name renewal status with Renewtron.</p>
            <div class="callout callout-warning"><p><strong>Not Due for Renewal Yet</strong></p><p>Your business name is currently active and not due for renewal at this time.</p></div>
            <table class="details"><tbody>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            </tbody></table>
            <p><strong>What does this mean?</strong></p>
            <ul>
            <li><p>Your business name registration is still valid</p></li>
            <li><p>No action is required from you at this time</p></li>
            <li><p>ASIC will notify you when your renewal period approaches</p></li>
            </ul>
            {% if reminder_opt_in %}
            <div class="callout callout-info"><p><strong>Reminder Set!</strong></p><p>We'll send you an email when your business name is due for renewal.</p></div>
            {% endif %}
            <p>When your business name becomes due for renewal, you can return to Renewtron and we'll process it quickly and easily.</p>
            """),

        new(NoBusinessNames, "ABN search: no business names",
            "Sent when the ABN they searched has no business names registered.",
            "lead",
            [FullName, Abn, Email],
            "ABN Search Results - No Business Names Found",
            "ABN Search Results",
            """
            <p>Hi {{ full_name }},</p>
            <p>Thank you for using Renewtron to check your business name status.</p>
            <div class="callout callout-note"><p><strong>No Business Names Found</strong></p><p>We couldn't find any business names registered under the ABN you provided.</p></div>
            <table class="details"><tbody>
            <tr><th>ABN Searched</th><td>{{ abn }}</td></tr>
            </tbody></table>
            <p><strong>This could mean:</strong></p>
            <ul>
            <li><p>The ABN doesn't have a registered business name</p></li>
            <li><p>The business name has been cancelled or deregistered</p></li>
            <li><p>There may be a typo in the ABN number</p></li>
            </ul>
            <p><strong>Need to register a business name?</strong></p>
            <p>You can register a new business name through ASIC at:</p>
            <div class="button-row"><a class="button" href="https://asic.gov.au/for-business/registering-a-business-name/">Register a Business Name</a></div>
            """),

        new(RenewalInProgress, "Renewal already in progress",
            "Sent when the ABN they searched already has a renewal being processed.",
            "lead",
            [FullName, Abn, Email],
            "Your Business Name Renewal Status",
            "Your Business Name Renewal Status",
            """
            <p>Hi {{ full_name }},</p>
            <p>Thank you for checking your business name renewal status with Renewtron.</p>
            <div class="callout callout-info"><p><strong>Renewal Already in Progress</strong></p><p>A renewal for this ABN is already being processed. This could be because you or someone else has recently initiated a renewal.</p></div>
            <table class="details"><tbody>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            </tbody></table>
            <p><strong>What should you do?</strong></p>
            <ul>
            <li><p>Wait for the current renewal to complete</p></li>
            <li><p>Check your email for any confirmation messages</p></li>
            <li><p>If you didn't initiate a renewal, please contact us</p></li>
            </ul>
            <p>If you have any concerns or questions about this renewal, please don't hesitate to contact us.</p>
            """),

        // Win-back's default is the wording saved in Settings before templates; see WinBackDefaults.
        new(WinBack, "Win-back",
            "Sent from Leads → Win-back to people who started a renewal and didn't finish.",
            "lead",
            [FullName, Abn, Email, new("business_name", "Business name", "Their first business name, or \"your business name\".", TemplateVariableKind.Text, "Citizen Plumbing")],
            "", "", ""),

        new(PaymentNotFinished, "Payment not finished",
            "Sent once, a couple of hours after someone opened the payment page and left without paying.",
            "lead",
            [FullName, Abn, Email,
             new("business_name", "Business name", "Their first business name, or \"your business name\".", TemplateVariableKind.Text, "Citizen Plumbing"),
             new("renew_link", "Link back", "The renewal site, to pick up where they left off.", TemplateVariableKind.Text, "https://businessnames.applyforanabn.au/")],
            "Finish renewing {{ business_name }}",
            "Your renewal isn't finished",
            """
            <p>Hi {{ full_name }},</p>
            <p>You were one step from renewing <strong>{{ business_name }}</strong>, but the payment didn't go through. Nothing has been charged.</p>
            <table class="details"><tbody>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            </tbody></table>
            <p>It takes about a minute to finish. Enter your ABN again and pick up where you left off.</p>
            <div class="button-row"><a class="button" href="{{ renew_link }}">Finish my renewal</a></div>
            <p>If you've already renewed, you can ignore this email.</p>
            """),

        new(RegistrationReceived, "Registration received",
            "Sent when a customer pays to register a new business name, before it goes to ASIC.",
            "registration",
            RegistrationVariables,
            "We've received your business name application - {{ business_name }}",
            "✓ Application Received",
            """
            <p>Hi {{ full_name }},</p>
            <p>Thank you, we've received your payment of <strong>{{ amount_paid }}</strong> to register <strong>{{ business_name }}</strong> for {{ registration_period }}.</p>
            <table class="details"><tbody>
            <tr><th>Business Name</th><td>{{ business_name }}</td></tr>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            <tr><th>Registration Period</th><td>{{ registration_period }}</td></tr>
            <tr><th>Amount Paid</th><td>{{ amount_paid }}</td></tr>
            </tbody></table>
            <p><strong>What happens next?</strong><br>We're lodging your application with ASIC now. We'll email you again with ASIC's reference once it's lodged.</p>
            """),

        new(RegistrationLodged, "Registration lodged",
            "Sent when we've lodged a new business name application with ASIC.",
            "registration",
            RegistrationVariables,
            "Your business name application is lodged - {{ business_name }}",
            "✓ Lodged with ASIC",
            """
            <p>Hi {{ full_name }},</p>
            <p>We've lodged your application to register <strong>{{ business_name }}</strong> with ASIC.</p>
            <table class="details"><tbody>
            <tr><th>Business Name</th><td>{{ business_name }}</td></tr>
            <tr><th>ABN</th><td>{{ abn }}</td></tr>
            <tr><th>Registration Period</th><td>{{ registration_period }}</td></tr>
            <tr><th>ASIC Reference</th><td>{{ transaction_reference }}</td></tr>
            </tbody></table>
            <p><strong>What happens next?</strong><br>ASIC will email the business name's record of registration and its ASIC key. Please keep this email for your records.</p>
            """),

        new(RegistrationRefused, "Registration refused",
            "Sent when ASIC won't take a new business name application (name taken, ABN refused). Refund the customer.",
            "registration",
            RegistrationVariables,
            "We couldn't register {{ business_name }}",
            "We couldn't register your business name",
            """
            <p>Hi {{ full_name }},</p>
            <p>We tried to lodge your application to register <strong>{{ business_name }}</strong> (ABN {{ abn }}), but ASIC wouldn't accept it:</p>
            <div class="callout callout-note"><p>{{ reason }}</p></div>
            <p>Nothing was lodged with ASIC. We'll refund your payment of <strong>{{ amount_paid }}</strong> in full. If you'd like to try a different name, reply to this email and we'll help.</p>
            """),
    ];

    public static EmailTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The sample values, as a model for previews and checks.</summary>
    public static Dictionary<string, object?> SampleModel(EmailTemplateDefinition d) =>
        d.Variables.ToDictionary(v => v.Name, v => (object?)v.Sample);
}
