using Renewtron.Data;
using Renewtron.Services.Email;

namespace Renewtron.Services;

public interface ILeadEmailService
{
    Task SendLeadCapturedEmailAsync(Lead lead);
    Task SendNotDueForRenewalEmailAsync(Lead lead);
    Task SendNoBusinessNamesEmailAsync(Lead lead);
    Task SendRenewalInProgressEmailAsync(Lead lead);
    /// <summary>Send the win-back email. firstBusinessName fills {{ business_name }}.
    /// <paramref name="outboundMessageId"/> is the row win-back already wrote for it, so the send records on that row.
    /// Throws when the win-back template is switched off, so that row is marked failed rather than sent.</summary>
    Task SendWinBackEmailAsync(Lead lead, string? firstBusinessName = null, Guid? outboundMessageId = null);

    /// <summary>The "payment not finished" follow-up. False when the template is switched off.</summary>
    Task<bool> SendPaymentNotFinishedEmailAsync(Lead lead, string? firstBusinessName, string renewLink);
}

/// <summary>Emails about a lead. The wording is in their templates (Emails → Templates); this fills in the values and sends.</summary>
public class LeadEmailService(IEmailSender sender, IEmailTemplates templates, ILogger<LeadEmailService> logger) : ILeadEmailService
{
    public Task SendLeadCapturedEmailAsync(Lead lead) => SendAsync(EmailTemplateCatalog.LeadCaptured, lead);
    public Task SendNotDueForRenewalEmailAsync(Lead lead) => SendAsync(EmailTemplateCatalog.NotDue, lead);
    public Task SendNoBusinessNamesEmailAsync(Lead lead) => SendAsync(EmailTemplateCatalog.NoBusinessNames, lead);
    public Task SendRenewalInProgressEmailAsync(Lead lead) => SendAsync(EmailTemplateCatalog.RenewalInProgress, lead);

    public async Task SendWinBackEmailAsync(Lead lead, string? firstBusinessName = null, Guid? outboundMessageId = null)
    {
        if (!await SendAsync(EmailTemplateCatalog.WinBack, lead, firstBusinessName, outboundMessageId))
            throw new InvalidOperationException("The win-back email is switched off in Emails → Templates.");
    }

    public Task<bool> SendPaymentNotFinishedEmailAsync(Lead lead, string? firstBusinessName, string renewLink) =>
        SendAsync(EmailTemplateCatalog.PaymentNotFinished, lead, firstBusinessName, extra: new() { ["renew_link"] = renewLink });

    /// <summary>False when the template is switched off (nothing sent).</summary>
    private async Task<bool> SendAsync(string key, Lead lead, string? firstBusinessName = null, Guid? messageId = null, Dictionary<string, object?>? extra = null)
    {
        var model = EmailModels.Lead(lead, firstBusinessName);
        foreach (var (k, v) in extra ?? []) model[k] = v;
        var email = await templates.RenderAsync(key, model);
        if (!email.Enabled)
        {
            logger.LogInformation("{Key} email not sent: the template is switched off", key);
            return false;
        }
        await sender.SendAsync(new EmailToSend(key, lead.Email, email.Subject, email.Text, email.Html,
            LeadId: lead.Id, ClientId: lead.ClientId, MessageId: messageId, TemplateKey: email.Key, TemplateVersion: email.Version));
        return true;
    }
}
