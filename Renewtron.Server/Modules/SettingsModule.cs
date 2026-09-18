using System.Text.RegularExpressions;
using Asic.Client.Abstractions;
using Carter;
using Renewtron.Abstractions;
using Renewtron.Settings;

namespace Renewtron.Modules;

public sealed class SettingsModule : ICarterModule
{
    // Secrets are write-only: GET returns a masked placeholder (last 4 kept for
    // recognition), and a PUT that round-trips the placeholder keeps the stored value.
    // The ASIC CVC never leaves the server at all.
    private const string MaskPrefix = "••••";

    private static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? "" : MaskPrefix + (value.Length > 4 ? value[^4..] : "");

    private static string Unmask(string? incoming, string? current) =>
        incoming != null && incoming.StartsWith(MaskPrefix) ? current ?? "" : incoming ?? "";

    private static readonly (string Label, string Sample, string Expected)[] PatternSamples =
    [
        ("key letter", "Here is the ASIC Key for YUMOHZY TRITS: 1-67783322838.\nYou can use the ASIC Key to link", "1-67783322838"),
        ("key letter, wrapped", "Here is the ASIC Key for BRM BUILDING REPAIRS & MAINTENANCE: 1-\n79565779188.\nYou can use", "1-79565779188"),
        ("renewal notice", "Business name renewal notice for 'M.R.A. CONCRETING'\nAccount Number: 85 101951857\nASIC Key: 1-43002328008\nDue Date:", "1-43002328008"),
        ("renewal confirmation", "Registration of business name renewed for 'BECKS BAKES'\nThe ASIC key for this business name is 1-56809537698.\nThank you", "1-56809537698"),
    ];

    // Shared by save and test so both see the same effective values.
    private static void NormalizeAsicKeyInbox(AsicKeyInboxSettings body, AsicKeyInboxSettings current)
    {
        body.Password = Unmask(body.Password, current.Password);
        body.ImapHost = string.IsNullOrWhiteSpace(body.ImapHost) ? "imap.gmail.com" : body.ImapHost.Trim();
        body.Username = (body.Username ?? "").Trim();
        if (body.ImapPort <= 0) body.ImapPort = 993;
        if (body.LookbackDays <= 0) body.LookbackDays = 30;
        if (string.IsNullOrWhiteSpace(body.Folder)) body.Folder = "INBOX";
        if (string.IsNullOrWhiteSpace(body.SubjectFilter)) body.SubjectFilter = "Notification request";
        if (string.IsNullOrWhiteSpace(body.AsicKeyPattern)) body.AsicKeyPattern = AsicKeyInboxSettings.DefaultAsicKeyPattern;
    }

    private static void NormalizeAsicKeyRequest(AsicKeyRequestSettings body, AsicKeyRequestSettings current)
    {
        body.TwoCaptchaApiKey = Unmask(body.TwoCaptchaApiKey, current.TwoCaptchaApiKey).Trim();
        body.RequestEmail = (body.RequestEmail ?? "").Trim();
        body.DefaultPhonePrefix = (body.DefaultPhonePrefix ?? "").Trim();
        body.DefaultPhoneNumber = (body.DefaultPhoneNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(body.MessageTemplate)) body.MessageTemplate = AsicKeyRequestSettings.DefaultMessageTemplate;
        if (body.MinCaptchaScore <= 0 || body.MinCaptchaScore > 1) body.MinCaptchaScore = 0.9;
        body.MaxCaptchaAttempts = Math.Clamp(body.MaxCaptchaAttempts <= 0 ? 3 : body.MaxCaptchaAttempts, 1, 5);
        body.MaxPerRun = Math.Clamp(body.MaxPerRun <= 0 ? 25 : body.MaxPerRun, 1, 500);
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings").RequireAuthorization().WithTags("Admin.Settings");

        group.MapGet("/", async (ISettingsService settings) =>
        {
            var sendGrid = await settings.GetSendGridSettingsAsync();
            var stripe = await settings.GetStripeSettingsAsync();
            var asic = await settings.GetAsicSettingsAsync();
            var ontraport = await settings.GetOntraportSettingsAsync();
            var asicKeyInbox = await settings.GetAsicKeyInboxSettingsAsync();
            var asicKeyRequest = await settings.GetAsicKeyRequestSettingsAsync();

            return Results.Ok(new
            {
                sendGrid = new { apiKey = Mask(sendGrid.ApiKey), fromEmail = sendGrid.FromEmail, fromName = sendGrid.FromName },
                stripe = new { secretKey = Mask(stripe.SecretKey), publishableKey = stripe.PublishableKey },
                pricing = await settings.GetPricingSettingsAsync(),
                asic = new
                {
                    forceFallback = asic.ForceFallback,
                    email = asic.Email,
                    cardNumber = Mask(asic.CardNumber),
                    cardholderName = asic.CardholderName,
                    expiryMonth = asic.ExpiryMonth,
                    expiryYear = asic.ExpiryYear,
                    // Deliberately never returned. Saving with an empty value keeps the stored one;
                    // hasCvc lets the UI show the section as complete without seeing the value.
                    cvc = "",
                    hasCvc = !string.IsNullOrEmpty(asic.Cvc),
                },
                ontraport = new
                {
                    apiAppId = Mask(ontraport.ApiAppId),
                    apiKey = Mask(ontraport.ApiKey),
                    conversationId = ontraport.ConversationId,
                    defaultPollingTimeoutSeconds = ontraport.DefaultPollingTimeoutSeconds,
                },
                winBack = await settings.GetWinBackSettingsAsync(),
                tracking = await settings.GetTrackingSettingsAsync(),
                asicKeyInbox = new
                {
                    enabled = asicKeyInbox.Enabled,
                    imapHost = asicKeyInbox.ImapHost,
                    imapPort = asicKeyInbox.ImapPort,
                    username = asicKeyInbox.Username,
                    password = Mask(asicKeyInbox.Password),
                    folder = asicKeyInbox.Folder,
                    subjectFilter = asicKeyInbox.SubjectFilter,
                    lookbackDays = asicKeyInbox.LookbackDays,
                    ontraportFieldId = asicKeyInbox.OntraportFieldId,
                    asicKeyPattern = asicKeyInbox.AsicKeyPattern,
                    // Saved overrides pin the pattern, so a newer code default needs a way back in.
                    defaultAsicKeyPattern = AsicKeyInboxSettings.DefaultAsicKeyPattern,
                },
                asicKeyRequest = new
                {
                    enabled = asicKeyRequest.Enabled,
                    autoRequestOnSync = asicKeyRequest.AutoRequestOnSync,
                    twoCaptchaApiKey = Mask(asicKeyRequest.TwoCaptchaApiKey),
                    minCaptchaScore = asicKeyRequest.MinCaptchaScore,
                    maxCaptchaAttempts = asicKeyRequest.MaxCaptchaAttempts,
                    requestEmail = asicKeyRequest.RequestEmail,
                    defaultPhonePrefix = asicKeyRequest.DefaultPhonePrefix,
                    defaultPhoneNumber = asicKeyRequest.DefaultPhoneNumber,
                    messageTemplate = asicKeyRequest.MessageTemplate,
                    maxPerRun = asicKeyRequest.MaxPerRun,
                    defaultMessageTemplate = AsicKeyRequestSettings.DefaultMessageTemplate,
                },
            });
        });

        group.MapPut("/sendgrid", async (SendGridSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetSendGridSettingsAsync();
            body.ApiKey = Unmask(body.ApiKey, current.ApiKey);
            await settings.UpdateSendGridSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/stripe", async (StripeSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetStripeSettingsAsync();
            body.SecretKey = Unmask(body.SecretKey, current.SecretKey);
            await settings.UpdateStripeSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/pricing", async (PricingSettings body, ISettingsService settings) =>
        {
            await settings.UpdatePricingSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/asic", async (AsicSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetAsicSettingsAsync();
            body.CardNumber = Unmask(body.CardNumber, current.CardNumber);
            if (string.IsNullOrEmpty(body.Cvc)) body.Cvc = current.Cvc;
            await settings.UpdateAsicSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/ontraport", async (OntraportSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetOntraportSettingsAsync();
            body.ApiAppId = Unmask(body.ApiAppId, current.ApiAppId);
            body.ApiKey = Unmask(body.ApiKey, current.ApiKey);
            await settings.UpdateOntraportSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/win-back", async (WinBackSettings body, ISettingsService settings) =>
        {
            await settings.UpdateWinBackSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/tracking", async (TrackingSettings body, ISettingsService settings) =>
        {
            await settings.UpdateTrackingSettingsAsync(body);
            return Results.NoContent();
        });

        group.MapPut("/asic-key-inbox", async (AsicKeyInboxSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetAsicKeyInboxSettingsAsync();
            NormalizeAsicKeyInbox(body, current);
            await settings.UpdateAsicKeyInboxSettingsAsync(body);
            return Results.NoContent();
        });

        // Exercises the form's values without saving them: IMAP login + folder + subject search,
        // the Ontraport field id against the Contact object's metadata, and the key regex.
        // Each check reports independently so a bad app password doesn't hide a bad field id.
        group.MapPost("/asic-key-inbox/test", async (
            AsicKeyInboxSettings body,
            ISettingsService settings,
            IAsicNotificationMailbox mailbox,
            IOntraportSalesService ontraport,
            CancellationToken ct) =>
        {
            var current = await settings.GetAsicKeyInboxSettingsAsync();
            NormalizeAsicKeyInbox(body, current);

            object mailboxCheck;
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            {
                mailboxCheck = new { ok = false, error = "Enter the Gmail address and app password first." };
            }
            else
            {
                try
                {
                    var probe = await mailbox.ProbeAsync(body, ct);
                    mailboxCheck = new
                    {
                        ok = true,
                        host = body.ImapHost,
                        username = body.Username,
                        folder = probe.Folder,
                        messagesInFolder = probe.MessagesInFolder,
                        matchingInLookback = probe.MatchingInLookback,
                        latestSubject = probe.LatestSubject,
                        latestReceivedAt = probe.LatestReceivedAt,
                    };
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    mailboxCheck = new { ok = false, error = ex.Message };
                }
            }

            object ontraportCheck;
            if (string.IsNullOrWhiteSpace(body.OntraportFieldId))
            {
                ontraportCheck = new { ok = false, error = "Ontraport field ID is empty — keys will be extracted but not written anywhere." };
            }
            else
            {
                try
                {
                    var alias = await ontraport.GetContactFieldAliasAsync(body.OntraportFieldId.Trim());
                    ontraportCheck = alias == null
                        ? new { ok = false, error = $"No field '{body.OntraportFieldId.Trim()}' exists on the Ontraport Contact object." }
                        : new { ok = true, fieldId = body.OntraportFieldId.Trim(), alias };
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ontraportCheck = new { ok = false, error = ex.Message };
                }
            }

            object patternCheck;
            try
            {
                if (string.IsNullOrWhiteSpace(body.AsicKeyPattern))
                    throw new ArgumentException("pattern is empty");
                var regex = new System.Text.RegularExpressions.Regex(body.AsicKeyPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline,
                    TimeSpan.FromSeconds(2));

                // Wording lifted from the three ASIC letters seen in the inbox so far; the
                // second one has the key wrapped across a line, as long names cause.
                var failures = new List<string>();
                foreach (var (label, sample, expected) in PatternSamples)
                {
                    var match = regex.Match(sample);
                    var extracted = match.Success
                        ? string.Concat((match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Where(c => !char.IsWhiteSpace(c)))
                        : null;
                    if (extracted != expected)
                        failures.Add(extracted == null ? $"{label}: no match" : $"{label}: captured '{extracted}', expected '{expected}'");
                }
                patternCheck = failures.Count == 0
                    ? new { ok = true, sampleKey = PatternSamples[0].Expected, samples = PatternSamples.Length }
                    : new { ok = false, error = "Pattern fails on real letter wording — " + string.Join("; ", failures) + "." };
            }
            catch (ArgumentException ex)
            {
                patternCheck = new { ok = false, error = $"Invalid regex: {ex.Message}" };
            }

            return Results.Ok(new { mailbox = mailboxCheck, ontraportField = ontraportCheck, keyPattern = patternCheck });
        });

        group.MapPut("/asic-key-request", async (AsicKeyRequestSettings body, ISettingsService settings) =>
        {
            var current = await settings.GetAsicKeyRequestSettingsAsync();
            NormalizeAsicKeyRequest(body, current);
            await settings.UpdateAsicKeyRequestSettingsAsync(body);
            return Results.NoContent();
        });

        // Checks the form's values without saving: the 2Captcha key (balance), the score
        // against ASIC's known minimum, the template's placeholders, and that the reply-to
        // address is the inbox the scanner actually reads.
        group.MapPost("/asic-key-request/test", async (
            AsicKeyRequestSettings body,
            ISettingsService settings,
            ICaptchaSolver captcha,
            CancellationToken ct) =>
        {
            var current = await settings.GetAsicKeyRequestSettingsAsync();
            NormalizeAsicKeyRequest(body, current);

            object captchaCheck;
            if (string.IsNullOrWhiteSpace(body.TwoCaptchaApiKey))
            {
                captchaCheck = new { ok = false, error = "Enter the 2Captcha API key first." };
            }
            else
            {
                try
                {
                    var balance = await captcha.GetBalanceAsync(body.TwoCaptchaApiKey, ct);
                    captchaCheck = balance > 0
                        ? new { ok = true, balance }
                        : new { ok = false, error = "2Captcha key is valid but the balance is $0 — top up before enabling." };
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    captchaCheck = new { ok = false, error = ex.Message };
                }
            }

            // ASIC's server-side check as observed in testing: "minimum score require : 0.5".
            object scoreCheck = body.MinCaptchaScore >= 0.5
                ? new { ok = true, minScore = body.MinCaptchaScore }
                : new { ok = false, error = $"Minimum score {body.MinCaptchaScore} is below ASIC's 0.5 threshold — tokens will be rejected. Use 0.7 or 0.9." };

            object templateCheck;
            var unknown = Regex.Matches(body.MessageTemplate, @"\{(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .Where(p => !new[] { "FirstName", "LastName", "Abn", "BusinessName", "Email" }.Contains(p, StringComparer.OrdinalIgnoreCase))
                .Distinct()
                .ToList();
            if (unknown.Count > 0)
            {
                templateCheck = new { ok = false, error = $"Unknown placeholder(s): {string.Join(", ", unknown.Select(u => "{" + u + "}"))}. Known: {{FirstName}} {{LastName}} {{Abn}} {{BusinessName}} {{Email}}." };
            }
            else if (!body.MessageTemplate.Contains("{Email}", StringComparison.OrdinalIgnoreCase))
            {
                templateCheck = new { ok = false, error = "The template never mentions {Email}, so ASIC won't know where to send the key." };
            }
            else
            {
                var sample = body.MessageTemplate
                    .Replace("{FirstName}", "Jane", StringComparison.OrdinalIgnoreCase)
                    .Replace("{LastName}", "Citizen", StringComparison.OrdinalIgnoreCase)
                    .Replace("{Abn}", "12345678901", StringComparison.OrdinalIgnoreCase)
                    .Replace("{BusinessName}", "EXAMPLE TRADING", StringComparison.OrdinalIgnoreCase)
                    .Replace("{Email}", body.RequestEmail, StringComparison.OrdinalIgnoreCase);
                templateCheck = new { ok = true, sample };
            }

            var inbox = await settings.GetAsicKeyInboxSettingsAsync();
            object emailCheck;
            if (string.IsNullOrWhiteSpace(body.RequestEmail))
                emailCheck = new { ok = false, error = "Request email is empty." };
            else if (string.IsNullOrWhiteSpace(inbox.Username))
                emailCheck = new { ok = false, error = $"Keys will be sent to {body.RequestEmail}, but no inbox is configured under ASIC key inbox to receive them." };
            else if (!string.Equals(inbox.Username.Trim(), body.RequestEmail, StringComparison.OrdinalIgnoreCase))
                emailCheck = new { ok = false, error = $"Keys will be sent to {body.RequestEmail}, but the scanner reads {inbox.Username.Trim()}. Use the same address (or an alias that lands in that inbox)." };
            else
                emailCheck = new { ok = true, email = body.RequestEmail };

            return Results.Ok(new { captcha = captchaCheck, score = scoreCheck, template = templateCheck, email = emailCheck });
        });
    }
}
