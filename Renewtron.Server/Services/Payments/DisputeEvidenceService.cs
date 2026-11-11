using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Data;
using Renewtron.Settings;
using Stripe;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Renewtron.Services;

public class DisputeEvidenceService : IDisputeEvidenceService
{
    /// <summary>Set on the dispute once we've staged evidence, so a resent webhook doesn't redo it.</summary>
    private const string StagedMetadataKey = "renewtron_evidence";

    /// <summary>Stripe caps all text evidence together at 150,000 characters; stay well inside.</summary>
    private const int MaxActivityLogChars = 40_000;

    private readonly ApplicationDbContext _db;
    private readonly StripeSettings _stripe;
    private readonly TestModeSettings _testMode;
    private readonly SendGridSettings _sendGrid;
    private readonly ILogger<DisputeEvidenceService> _logger;

    public DisputeEvidenceService(
        ApplicationDbContext db,
        IOptionsSnapshot<StripeSettings> stripe,
        IOptionsSnapshot<TestModeSettings> testMode,
        IOptionsSnapshot<SendGridSettings> sendGrid,
        ILogger<DisputeEvidenceService> logger)
    {
        _db = db;
        _stripe = stripe.Value;
        _testMode = testMode.Value;
        _sendGrid = sendGrid.Value;
        _logger = logger;
    }

    private RequestOptions Options(bool testMode)
    {
        if (testMode && !_testMode.StripeReady)
            throw new InvalidOperationException("Test mode needs Stripe test keys (Settings → Test mode).");
        return new RequestOptions { ApiKey = testMode ? _testMode.StripeSecretKey : _stripe.SecretKey };
    }

    public async Task<DisputeEvidenceResult> PrepareEvidenceAsync(string disputeId, bool force = false, bool testMode = false)
    {
        var options = Options(testMode);
        var disputes = new DisputeService();
        var dispute = await disputes.GetAsync(disputeId, new DisputeGetOptions { Expand = ["charge"] }, options);

        // Only a dispute still waiting on us can take evidence; a submitted one is locked.
        if (dispute.Status is not ("needs_response" or "warning_needs_response"))
            return Done(dispute.Id, $"Dispute is '{dispute.Status}', nothing to respond to.");
        if (dispute.EvidenceDetails?.SubmissionCount > 0)
            return Done(dispute.Id, "Evidence was already submitted.");
        if (!force && dispute.Metadata?.ContainsKey(StagedMetadataKey) == true)
            return Done(dispute.Id, "Renewtron already staged evidence on this dispute.");

        var renewals = await FindRenewalsAsync(dispute, testMode);
        if (renewals.Count == 0)
        {
            // Smart Disputes still builds its own packet from Stripe's data.
            _logger.LogWarning("Dispute {DisputeId}: no renewal matches payment {PaymentIntentId} or the charge's email",
                dispute.Id, dispute.PaymentIntentId);
            return Done(dispute.Id, "No renewal matches this charge; left to Smart Disputes.");
        }

        var lead = renewals.Select(r => r.Lead).FirstOrDefault(l => l is not null);
        var activity = await ActivityLogAsync(lead);
        var completed = renewals.Where(r => r.Status == RenewalStatus.Completed && r.CompletedAt is not null).ToList();

        var files = new FileService();
        var serviceRecord = await UploadPdfAsync(files, "service-record.pdf", "Service record", ServiceRecordLines(dispute, renewals, lead, activity), options);
        string? communication = null;
        if (completed.Count > 0)
            communication = await UploadPdfAsync(files, "confirmation-emails.pdf", "Customer communication", ConfirmationEmailLines(completed), options);

        var email = lead?.Email ?? renewals.Select(r => r.Email).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        var evidence = new DisputeEvidenceOptions
        {
            CustomerName = lead?.FullName ?? dispute.Charge?.BillingDetails?.Name,
            CustomerEmailAddress = email,
            CustomerPurchaseIp = lead?.IpAddress,
            ProductDescription = ProductDescription(renewals),
            ServiceDate = completed.Count > 0 ? completed.Min(r => r.CompletedAt)!.Value.ToString("yyyy-MM-dd") : null,
            AccessActivityLog = activity.Count > 0 ? Truncate(string.Join("\n", activity), MaxActivityLogChars) : null,
            UncategorizedText = Summary(renewals, lead),
            ServiceDocumentation = serviceRecord,
            CustomerCommunication = communication,
        };

        var update = new DisputeUpdateOptions
        {
            Evidence = evidence,
            Submit = false, // stage only: Smart Disputes (or a person) submits before the deadline
            Metadata = new Dictionary<string, string>
            {
                [StagedMetadataKey] = DateTime.UtcNow.ToString("u"),
                ["renewtron_renewal_ids"] = Truncate(string.Join(",", renewals.Select(r => r.Id)), 500),
            },
        };
        // Stripe merges our evidence with its Smart Disputes packet and submits the stronger one.
        // Stripe.net 51 predates the field, so it goes as an extra parameter.
        update.AddExtraParam("intended_submission_method", "prefer_smart_disputes");

        await disputes.UpdateAsync(dispute.Id, update, options);

        _logger.LogInformation("Dispute {DisputeId}: staged evidence from {Count} renewal(s) for {Email}",
            dispute.Id, renewals.Count, email);
        return new DisputeEvidenceResult(dispute.Id, true,
            $"Evidence staged from {renewals.Count} renewal(s); Smart Disputes submits it before the deadline.", renewals.Count);
    }

    private static DisputeEvidenceResult Done(string disputeId, string message) => new(disputeId, false, message, 0);

    /// <summary>
    /// A site payment is matched by its PaymentIntent (a batch of names shares one). Anything
    /// else — an Ontraport sale charged through the same Stripe account — is matched by the
    /// charge's email to renewals started within 60 days after the charge.
    /// </summary>
    private async Task<List<RenewalRequest>> FindRenewalsAsync(Dispute dispute, bool testMode)
    {
        IQueryable<RenewalRequest> Query() => _db.RenewalRequests.AsNoTracking()
            .Include(r => r.SearchResult).ThenInclude(s => s.SearchLog)
            .Include(r => r.Lead)
            .Where(r => r.IsTest == testMode); // a test-mode dispute is a test customer's

        if (!string.IsNullOrEmpty(dispute.PaymentIntentId))
        {
            var byIntent = await Query()
                .Where(r => r.StripePayment != null && r.StripePayment.PaymentIntentId == dispute.PaymentIntentId)
                .ToListAsync();
            if (byIntent.Count > 0) return byIntent;
        }

        var charge = dispute.Charge;
        var email = (charge?.BillingDetails?.Email ?? charge?.ReceiptEmail)?.Trim();
        if (string.IsNullOrEmpty(email) || charge is null) return [];

        var from = charge.Created.AddDays(-1);
        var to = charge.Created.AddDays(60);
        return await Query()
            .Where(r => r.Email == email || (r.Lead != null && r.Lead.Email == email))
            .Where(r => r.InitiatedAt >= from && r.InitiatedAt <= to)
            .ToListAsync();
    }

    /// <summary>Every step the customer took in the wizard, with the IP and browser it came from.</summary>
    private async Task<List<string>> ActivityLogAsync(Lead? lead)
    {
        if (lead is null) return [];

        var events = await _db.FunnelEvents.AsNoTracking()
            .Where(e => e.LeadId == lead.Id || (lead.VisitorId != null && e.VisitorId == lead.VisitorId))
            .OrderBy(e => e.CreatedAt)
            .Take(500)
            .ToListAsync();

        var lines = new List<string>
        {
            $"{lead.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC  Customer entered their details ({lead.FullName}, {lead.Email}, {lead.MobileNumber})  IP {lead.IpAddress ?? "unknown"}  {lead.UserAgent}",
        };
        lines.AddRange(events.Select(e =>
            $"{e.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC  {FunnelSteps.LabelFor(e.Step)}" +
            (string.IsNullOrWhiteSpace(e.Detail) ? "" : $" ({e.Detail})") +
            $"  IP {e.IpAddress ?? "unknown"}  {e.UserAgent}"));
        return lines.OrderBy(l => l, StringComparer.Ordinal).ToList();
    }

    private string Brand => string.IsNullOrWhiteSpace(_sendGrid.FromName) ? "our service" : _sendGrid.FromName;

    private string ProductDescription(List<RenewalRequest> renewals)
    {
        var names = string.Join(", ", renewals.Select(r =>
            $"{r.SearchResult?.BusinessName} (ABN {r.SearchResult?.SearchLog?.Abn}, {r.RenewalYears} year{(r.RenewalYears == 1 ? "" : "s")})"));
        return $"Renewal of Australian business name registration(s) with ASIC (Australian Securities & Investments Commission), " +
               $"lodged with ASIC on the customer's behalf by {Brand}. The customer looked up their ABN, chose the names to renew " +
               $"and the renewal period, and paid online. Names renewed: {names}.";
    }

    private static string Summary(List<RenewalRequest> renewals, Lead? lead)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The service was delivered: each business name below was renewed with ASIC, the Australian government register, which issued a transaction reference.");
        foreach (var r in renewals)
        {
            sb.Append($"- {r.SearchResult?.BusinessName} (ABN {r.SearchResult?.SearchLog?.Abn}): {r.Status}");
            if (r.CompletedAt is not null) sb.Append($", renewed {r.CompletedAt:yyyy-MM-dd HH:mm} UTC");
            if (!string.IsNullOrEmpty(r.TransactionReference)) sb.Append($", ASIC transaction reference {r.TransactionReference}");
            sb.AppendLine($", A${r.Amount:F2} for {r.RenewalYears} year{(r.RenewalYears == 1 ? "" : "s")}.");
        }
        if (lead is not null)
            sb.AppendLine($"The order was placed by {lead.FullName} ({lead.Email}, {lead.MobileNumber}), who entered their own date of birth and contact details, from IP {lead.IpAddress ?? "unknown"}.");
        sb.AppendLine("A renewal confirmation email with the ASIC transaction reference was sent to the customer's email when each renewal completed. The customer can verify the renewal on ASIC Connect (connectonline.asic.gov.au) by business name.");
        return sb.ToString();
    }

    private List<string> ServiceRecordLines(Dispute dispute, List<RenewalRequest> renewals, Lead? lead, List<string> activity)
    {
        var lines = new List<string>
        {
            $"Service provider: {Brand}",
            $"Stripe dispute: {dispute.Id}   Payment: {dispute.PaymentIntentId ?? dispute.ChargeId}   Reason: {dispute.Reason}",
            "",
            "CUSTOMER",
        };
        if (lead is not null)
        {
            lines.Add($"Name: {lead.FullName}");
            lines.Add($"Email: {lead.Email}");
            lines.Add($"Mobile: {lead.MobileNumber}");
            lines.Add($"ABN: {lead.Abn}");
            lines.Add($"Order placed: {lead.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC from IP {lead.IpAddress ?? "unknown"}");
        }
        else
        {
            lines.Add($"Email: {renewals.Select(r => r.Email).FirstOrDefault(e => e is not null)}");
        }

        lines.Add("");
        lines.Add("BUSINESS NAMES RENEWED WITH ASIC");
        foreach (var r in renewals)
        {
            lines.Add($"{r.SearchResult?.BusinessName}  (ABN {r.SearchResult?.SearchLog?.Abn})");
            lines.Add($"    Period: {r.RenewalYears} year(s)   Amount: A${r.Amount:F2}   Status: {r.Status}");
            lines.Add($"    Ordered: {r.InitiatedAt:yyyy-MM-dd HH:mm} UTC   Renewed: {(r.CompletedAt is null ? "-" : $"{r.CompletedAt:yyyy-MM-dd HH:mm} UTC")}");
            if (!string.IsNullOrEmpty(r.TransactionReference))
                lines.Add($"    ASIC transaction reference: {r.TransactionReference}");
        }

        if (activity.Count > 0)
        {
            lines.Add("");
            lines.Add("CUSTOMER ACTIVITY ON OUR WEBSITE");
            lines.AddRange(activity);
        }
        return lines;
    }

    /// <summary>The confirmation email each completed renewal sends (EmailService.SendRenewalConfirmationAsync).</summary>
    private List<string> ConfirmationEmailLines(List<RenewalRequest> completed)
    {
        var lines = new List<string>();
        foreach (var r in completed)
        {
            var name = r.SearchResult?.BusinessName;
            lines.AddRange(
            [
                $"From: {_sendGrid.FromName} <{_sendGrid.FromEmail}>",
                $"To: {r.Email ?? r.Lead?.Email}",
                $"Sent: on completion, {r.CompletedAt:yyyy-MM-dd HH:mm} UTC",
                $"Subject: Business Name Renewal Confirmation - {name}",
                "",
                "Your business name has been successfully renewed with ASIC.",
                $"Business Name: {name}",
                $"ABN: {r.SearchResult?.SearchLog?.Abn}",
                $"Renewal Period: {r.RenewalYears} {(r.RenewalYears == 1 ? "Year" : "Years")}",
                $"Amount Paid: ${r.Amount:F2}",
                $"Transaction Reference: {r.TransactionReference ?? "N/A"}",
                "",
                "Your business name registration has been extended and is now active with ASIC.",
                "",
                new string('-', 60),
                "",
            ]);
        }
        return lines;
    }

    private static async Task<string> UploadPdfAsync(FileService files, string fileName, string title, List<string> lines, RequestOptions options)
    {
        using var stream = new MemoryStream(TextPdf(title, lines));
        var file = await files.CreateAsync(new FileCreateOptions
        {
            File = new MultipartFileContent { Data = stream, Name = fileName, Type = "application/pdf" },
            Purpose = FilePurpose.DisputeEvidence,
        }, options);
        return file.Id;
    }

    /// <summary>A plain A4 text document: a title, then the lines wrapped and paged.</summary>
    private static byte[] TextPdf(string title, List<string> lines)
    {
        const double margin = 50, fontSize = 9, lineHeight = 12, wrapAt = 105;
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        var page = builder.AddPage(PageSize.A4);
        var top = page.PageSize.Height - margin;
        var y = top;
        page.AddText(Latin(title), 14, new PdfPoint(margin, y), bold);
        y -= 24;

        foreach (var line in lines.SelectMany(l => Wrap(Latin(l), (int)wrapAt)))
        {
            if (y < margin)
            {
                page = builder.AddPage(PageSize.A4);
                y = top;
            }
            if (line.Length > 0) page.AddText(line, fontSize, new PdfPoint(margin, y), font);
            y -= lineHeight;
        }
        return builder.Build();
    }

    private static IEnumerable<string> Wrap(string line, int width)
    {
        if (line.Length <= width) { yield return line; yield break; }
        var rest = line;
        while (rest.Length > width)
        {
            var cut = rest.LastIndexOf(' ', width);
            if (cut <= 0) cut = width;
            yield return rest[..cut];
            rest = "    " + rest[cut..].TrimStart();
        }
        yield return rest;
    }

    /// <summary>The standard PDF fonts throw on characters outside their encoding; keep to plain ASCII.</summary>
    private static string Latin(string? s) =>
        new((s ?? "").Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
