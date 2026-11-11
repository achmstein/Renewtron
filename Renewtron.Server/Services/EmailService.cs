using Microsoft.EntityFrameworkCore;
using Renewtron.Data;
using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Services.Email;
using Renewtron.Settings;

namespace Renewtron.Services;

/// <summary>Renewal emails. The wording is in their templates (Emails → Templates); this fills in the values and sends.</summary>
public class EmailService : IEmailService
{
    private readonly IEmailSender _sender;
    private readonly IEmailTemplates _templates;
    private readonly ApplicationDbContext _db;
    private readonly PortalSettings _portal;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IEmailSender sender, IEmailTemplates templates, ApplicationDbContext db,
        IOptionsSnapshot<PortalSettings> portal, ILogger<EmailService> logger)
    {
        _sender = sender;
        _templates = templates;
        _db = db;
        _portal = portal.Value;
        _logger = logger;
    }

    /// <summary>The client a lead (or a renewal's lead) belongs to, for the email's record.</summary>
    private async Task<Guid?> ClientOfAsync(Guid? leadId, Guid? renewalRequestId)
    {
        if (renewalRequestId is { } rid
            && await _db.RenewalRequests.Where(r => r.Id == rid).Select(r => new { r.ClientId, r.LeadId }).FirstOrDefaultAsync() is { } r)
        {
            if (r.ClientId is { } c) return c;
            leadId ??= r.LeadId;
        }
        return leadId is { } lid ? await _db.Leads.Where(l => l.Id == lid).Select(l => l.ClientId).FirstOrDefaultAsync() : null;
    }

    public async Task SendRenewalConfirmationAsync(
        string toEmail,
        string businessName,
        string abn,
        int renewalYears,
        decimal amountPaid,
        string transactionReference,
        Guid? renewalRequestId = null,
        Guid? leadId = null)
    {
        var model = EmailModels.RenewalConfirmation(_portal, toEmail, businessName, abn, renewalYears, amountPaid, transactionReference, DateTime.UtcNow);
        var email = await _templates.RenderAsync(EmailTemplateCatalog.RenewalConfirmation, model);
        if (!email.Enabled)
        {
            _logger.LogInformation("Renewal confirmation not sent: the template is switched off");
            return;
        }

        if (renewalRequestId is { } rr && leadId is null)
            leadId = await _db.RenewalRequests.Where(r => r.Id == rr).Select(r => r.LeadId).FirstOrDefaultAsync();
        await _sender.SendAsync(new EmailToSend(EmailTemplateCatalog.RenewalConfirmation, toEmail, email.Subject, email.Text, email.Html,
            LeadId: leadId, ClientId: await ClientOfAsync(leadId, renewalRequestId), RenewalRequestId: renewalRequestId,
            TemplateKey: email.Key, TemplateVersion: email.Version));
    }

    public async Task SendRegistrationEmailAsync(string templateKey, Guid registrationId, string? reason = null)
    {
        var reg = await _db.BusinessNameRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registrationId);
        if (reg is null || string.IsNullOrWhiteSpace(reg.Email)) return;
        var model = EmailModels.Registration(reg, reason, DateTime.UtcNow);
        var email = await _templates.RenderAsync(templateKey, model);
        if (!email.Enabled)
        {
            _logger.LogInformation("Registration email {Key} not sent: the template is switched off", templateKey);
            return;
        }
        await _sender.SendAsync(new EmailToSend(templateKey, reg.Email, email.Subject, email.Text, email.Html,
            TemplateKey: email.Key, TemplateVersion: email.Version));
    }

    public Task SendPaidEarlyAsync(string toEmail, string abn, string[] businessNames, int renewalYears, decimal amountPaid) =>
        SendPaidEarlyAsync(toEmail, abn, businessNames, renewalYears, amountPaid, null);

    public async Task SendPaidEarlyAsync(string toEmail, string abn, string[] businessNames, int renewalYears, decimal amountPaid, Guid? leadId)
    {
        var model = EmailModels.PaidEarly(toEmail, abn, businessNames, renewalYears, amountPaid);
        var email = await _templates.RenderAsync(EmailTemplateCatalog.PaidEarly, model);
        if (!email.Enabled)
        {
            _logger.LogInformation("Paid-early email not sent: the template is switched off");
            return;
        }

        await _sender.SendAsync(new EmailToSend(EmailTemplateCatalog.PaidEarly, toEmail, email.Subject, email.Text, email.Html,
            LeadId: leadId, ClientId: await ClientOfAsync(leadId, null), TemplateKey: email.Key, TemplateVersion: email.Version));
    }
}
