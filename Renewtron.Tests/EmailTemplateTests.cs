using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Services.Email;
using Renewtron.Settings;

namespace Renewtron.Tests;

/// <summary>
/// Email templates. The "golden" tests hold every line the plain-text part said before templates (taken from
/// the hand-written emails these replaced), so moving them into templates didn't change what customers read.
/// </summary>
public class EmailTemplateTests
{
    private static TemplateContent Default(string key) => Store(NewDb()).DefaultFor(key);

    private static async Task<RenderedEmail> RenderDefault(string key, IReadOnlyDictionary<string, object?> model) =>
        await EmailTemplateRenderer.RenderAsync(Default(key), EmailLayout.Default, model);

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EmailTemplates Store(ApplicationDbContext db, WinBackSettings? winBack = null) =>
        new(db, new StaticMonitor<WinBackSettings>(winBack ?? new WinBackSettings()), NullLogger<EmailTemplates>.Instance);

    private static Lead Lead(bool reminder = false) => new()
    {
        Id = Guid.NewGuid(), FullName = "Jane Citizen", Email = "jane@example.com", Abn = "51824753556",
        CreatedAt = new DateTime(2026, 11, 9, 1, 0, 0, DateTimeKind.Utc), ReminderOptIn = reminder,
    };

    private static void HasLines(string text, params string[] lines)
    {
        foreach (var line in lines)
            Assert.True(text.Contains(line), $"Missing \"{line}\" in:\n{text}");
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Every_default_passes_its_own_check(string key)
    {
        var d = EmailTemplateCatalog.Find(key)!;
        var problems = await EmailTemplateRenderer.CheckAsync(d, Default(key), EmailLayout.Default);
        Assert.Empty(problems);
    }

    public static TheoryData<string> Keys() => new(EmailTemplateCatalog.All.Select(d => d.Key));

    [Fact]
    public async Task Layout_default_passes_its_check() =>
        Assert.Empty(await EmailTemplateRenderer.CheckLayoutAsync(EmailLayout.Default));

    [Fact]
    public async Task Renewal_confirmation_says_what_it_always_said()
    {
        var portal = new PortalSettings { BaseUrl = "https://portal.example.com" };
        var model = EmailModels.RenewalConfirmation(portal, "jane@example.com", "Citizen Plumbing", "51824753556", 1, 97m, "7E1234567",
            new DateTime(2026, 11, 9, 10, 15, 0, DateTimeKind.Utc));
        var email = await RenderDefault(EmailTemplateCatalog.RenewalConfirmation, model);

        Assert.True(email.Ok);
        Assert.Equal("Business Name Renewal Confirmation - Citizen Plumbing", email.Subject);
        HasLines(email.Text,
            "Renewal Successful",
            "Your business name has been successfully renewed with ASIC.",
            "Business Name: Citizen Plumbing",
            "ABN: 51824753556",
            "Renewal Period: 1 Year",
            "Amount Paid: $97.00",
            "Transaction Reference: 7E1234567",
            "Date: November 09, 2026 at 10:15 AM UTC",
            "Please keep this email for your records.",
            "---",
            "This is an automated email from Renewtron.");
        // Without a signing key there's no one-time link: the button opens the sign-in page.
        Assert.Contains("View your business names in your Business Portal: https://portal.example.com", email.Text);
        Assert.DoesNotContain("works once, for 72 hours", email.Text);
    }

    [Fact]
    public async Task Paid_early_reads_right_for_one_name_and_for_several()
    {
        var one = await RenderDefault(EmailTemplateCatalog.PaidEarly,
            EmailModels.PaidEarly("j@x.com", "51824753556", ["Citizen Plumbing"], 1, 97m));
        Assert.Equal("Payment received - Citizen Plumbing renewal", one.Subject);
        HasLines(one.Text,
            "Thank you, we've received your payment of $97.00 to renew your business name for 1 year (ABN 51824753556):",
            "Citizen Plumbing",
            "as soon as its renewal opens.",
            "once it has been renewed. Please keep this email for your records.");

        var two = await RenderDefault(EmailTemplateCatalog.PaidEarly,
            EmailModels.PaidEarly("j@x.com", "51824753556", ["Citizen Plumbing", "Citizen Gas"], 3, 291m));
        Assert.Equal("Payment received - renewal of 2 business names", two.Subject);
        HasLines(two.Text,
            "to renew your business names for 3 years",
            "Citizen Plumbing", "Citizen Gas",
            "as soon as their renewal opens.",
            "once they have been renewed.");
    }

    [Fact]
    public async Task Lead_emails_say_what_they_always_said()
    {
        var captured = await RenderDefault(EmailTemplateCatalog.LeadCaptured, EmailModels.Lead(Lead()));
        Assert.Equal("Thank You for Using Renewtron", captured.Subject);
        HasLines(captured.Text, "Hi Jane Citizen,", "ABN: 51 824 753 556", "Date: November 09, 2026",
            "We've saved your details and will be here whenever you need to renew your business name.");

        var noNames = await RenderDefault(EmailTemplateCatalog.NoBusinessNames, EmailModels.Lead(Lead()));
        HasLines(noNames.Text, "No Business Names Found", "ABN Searched: 51 824 753 556",
            "- There may be a typo in the ABN number",
            "Register a Business Name: https://asic.gov.au/for-business/registering-a-business-name/");

        var inProgress = await RenderDefault(EmailTemplateCatalog.RenewalInProgress, EmailModels.Lead(Lead()));
        HasLines(inProgress.Text, "Renewal Already in Progress", "- Wait for the current renewal to complete");
    }

    [Fact]
    public async Task Not_due_shows_the_reminder_only_to_people_who_asked()
    {
        var asked = await RenderDefault(EmailTemplateCatalog.NotDue, EmailModels.Lead(Lead(reminder: true)));
        var didnt = await RenderDefault(EmailTemplateCatalog.NotDue, EmailModels.Lead(Lead(reminder: false)));
        Assert.Contains("Reminder Set!", asked.Text);
        Assert.DoesNotContain("Reminder Set!", didnt.Text);
        HasLines(didnt.Text, "Not Due for Renewal Yet", "- No action is required from you at this time");
    }

    [Fact]
    public async Task Customer_values_are_encoded_in_html_and_kept_out_of_the_subjects_line_breaks()
    {
        var portal = new PortalSettings();
        var model = EmailModels.RenewalConfirmation(portal, "j@x.com", "Tom & Jerry <script>\r\nBcc: x", "1", 1, 1m, "r", DateTime.UtcNow);
        var email = await RenderDefault(EmailTemplateCatalog.RenewalConfirmation, model);

        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("Tom &amp; Jerry &lt;script&gt;", email.Html);
        Assert.Contains("Business Name: Tom & Jerry <script>", email.Text);
        Assert.DoesNotContain("\n", email.Subject);
        Assert.DoesNotContain("\r", email.Subject);
    }

    [Fact]
    public async Task Styles_are_copied_onto_elements()
    {
        var email = await RenderDefault(EmailTemplateCatalog.NoBusinessNames, EmailModels.Lead(Lead()));
        Assert.Matches("<a[^>]*class=\"button\"[^>]*style=\"[^\"]*background-color: #4F46E5", email.Html);
    }

    [Fact]
    public async Task Check_catches_typos_bad_liquid_and_unknown_filters()
    {
        var d = EmailTemplateCatalog.Find(EmailTemplateCatalog.LeadCaptured)!;
        var typo = await EmailTemplateRenderer.CheckAsync(d, new("Hi {{ ful_name }}", "", "<p>x</p>"), EmailLayout.Default);
        Assert.Contains(typo, p => p.Field == "subject" && p.Message.Contains("ful_name"));

        var unclosed = await EmailTemplateRenderer.CheckAsync(d, new("s", "", "{% if full_name %}<p>x</p>"), EmailLayout.Default);
        Assert.Contains(unclosed, p => p.Field == "body");

        var filter = await EmailTemplateRenderer.CheckAsync(d, new("s", "", "<p>{{ full_name | shout }}</p>"), EmailLayout.Default);
        Assert.Contains(filter, p => p.Field == "body");
    }

    [Fact]
    public async Task Check_looks_inside_both_sides_of_a_show_only_if()
    {
        var d = EmailTemplateCatalog.Find(EmailTemplateCatalog.NotDue)!;
        // The sample has reminder_opt_in = true, so the typo is only reached with it false.
        var body = "{% if reminder_opt_in %}<p>ok</p>{% else %}<p>{{ abnn }}</p>{% endif %}";
        var problems = await EmailTemplateRenderer.CheckAsync(d, new("s", "", body), EmailLayout.Default);
        Assert.Contains(problems, p => p.Message.Contains("abnn"));
    }

    [Fact]
    public async Task Layout_without_content_is_refused() =>
        Assert.Contains(await EmailTemplateRenderer.CheckLayoutAsync("<html><body>hi</body></html>"), p => p.Field == "layout");

    [Fact]
    public async Task Win_back_starts_from_the_settings_wording_with_tags_converted()
    {
        var settings = new WinBackSettings
        {
            Subject = "{{FullName}}, finish your renewal",
            BodyPlain = "Hi {{FullName}},\n\nABN {{abn}} for {{BusinessName}}.\nGo: https://example.com/renew.\n",
        };
        var content = Store(NewDb(), settings).DefaultFor(EmailTemplateCatalog.WinBack);
        Assert.Equal("{{ full_name }}, finish your renewal", content.Subject);
        Assert.Contains("<a href=\"https://example.com/renew\">", content.Body);

        var email = await EmailTemplateRenderer.RenderAsync(content, EmailLayout.Default, EmailModels.Lead(Lead(), "Citizen Plumbing"));
        Assert.Equal("Jane Citizen, finish your renewal", email.Subject);
        HasLines(email.Text, "Hi Jane Citizen,", "ABN 51 824 753 556 for Citizen Plumbing.", "Go: https://example.com/renew.");
        Assert.Empty(await EmailTemplateRenderer.CheckAsync(EmailTemplateCatalog.Find(EmailTemplateCatalog.WinBack)!, content, EmailLayout.Default));
    }

    [Fact]
    public async Task Saved_versions_count_up_and_the_latest_is_sent()
    {
        await using var db = NewDb();
        var store = Store(db);
        Assert.Equal(0, (await store.CurrentAsync(EmailTemplateCatalog.LeadCaptured)).Version);

        var v1 = await store.SaveAsync(EmailTemplateCatalog.LeadCaptured, new("Hello {{ full_name }}", "Hi", "<p>One</p>"), "david", null);
        var v2 = await store.SaveAsync(EmailTemplateCatalog.LeadCaptured, new("Hello again {{ full_name }}", "Hi", "<p>Two</p>"), "david", "tweak");
        Assert.Equal((1, 2), (v1, v2));

        var email = await store.RenderAsync(EmailTemplateCatalog.LeadCaptured, EmailModels.Lead(Lead()));
        Assert.Equal((2, "Hello again Jane Citizen"), (email.Version, email.Subject));
        Assert.Contains("Two", email.Text);
    }

    [Fact]
    public async Task A_switched_off_email_is_not_rendered()
    {
        await using var db = NewDb();
        var store = Store(db);
        await store.SetEnabledAsync(EmailTemplateCatalog.LeadCaptured, false, "david");
        var email = await store.RenderAsync(EmailTemplateCatalog.LeadCaptured, EmailModels.Lead(Lead()));
        Assert.False(email.Enabled);
        Assert.False(await store.IsEnabledAsync(EmailTemplateCatalog.LeadCaptured));
        Assert.True(await store.IsEnabledAsync(EmailTemplateCatalog.NotDue));
    }

    [Fact]
    public async Task A_saved_version_that_no_longer_renders_falls_back_to_the_default()
    {
        await using var db = NewDb();
        db.EmailTemplateVersions.Add(new EmailTemplateVersion
        {
            TemplateKey = EmailTemplateCatalog.LeadCaptured, Version = 1, Subject = "{{ gone }}", Heading = "", Body = "<p>x</p>",
        });
        await db.SaveChangesAsync();

        var email = await Store(db).RenderAsync(EmailTemplateCatalog.LeadCaptured, EmailModels.Lead(Lead()));
        Assert.Equal((0, "Thank You for Using Renewtron"), (email.Version, email.Subject));
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
