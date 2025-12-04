using Microsoft.Extensions.Options;
using Renewtron.Data;
using Renewtron.Settings;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Renewtron.Services;

public interface ILeadEmailService
{
    Task SendLeadCapturedEmailAsync(Lead lead);
    Task SendNotDueForRenewalEmailAsync(Lead lead);
    Task SendNoBusinessNamesEmailAsync(Lead lead);
    Task SendRenewalInProgressEmailAsync(Lead lead);
}

public class LeadEmailService : ILeadEmailService
{
    private readonly SendGridClient _client;
    private readonly SendGridSettings _settings;

    public LeadEmailService(IOptionsSnapshot<SendGridSettings> settings)
    {
        _settings = settings.Value;
        _client = new SendGridClient(_settings.ApiKey);
    }

    public async Task SendLeadCapturedEmailAsync(Lead lead)
    {
        var subject = "Thank You for Using Renewtron";
        var htmlContent = GetLeadCapturedHtml(lead);
        var plainContent = GetLeadCapturedPlain(lead);

        await SendEmailAsync(lead.Email, subject, plainContent, htmlContent);
    }

    public async Task SendNotDueForRenewalEmailAsync(Lead lead)
    {
        var subject = "Your Business Name Registration Status";
        var htmlContent = GetNotDueForRenewalHtml(lead);
        var plainContent = GetNotDueForRenewalPlain(lead);

        await SendEmailAsync(lead.Email, subject, plainContent, htmlContent);
    }

    public async Task SendNoBusinessNamesEmailAsync(Lead lead)
    {
        var subject = "ABN Search Results - No Business Names Found";
        var htmlContent = GetNoBusinessNamesHtml(lead);
        var plainContent = GetNoBusinessNamesPlain(lead);

        await SendEmailAsync(lead.Email, subject, plainContent, htmlContent);
    }

    public async Task SendRenewalInProgressEmailAsync(Lead lead)
    {
        var subject = "Your Business Name Renewal Status";
        var htmlContent = GetRenewalInProgressHtml(lead);
        var plainContent = GetRenewalInProgressPlain(lead);

        await SendEmailAsync(lead.Email, subject, plainContent, htmlContent);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string plainContent, string htmlContent)
    {
        var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
        var to = new EmailAddress(toEmail);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainContent, htmlContent);
        await _client.SendEmailAsync(msg);
    }

    private string FormatAbn(string abn)
    {
        var clean = abn.Replace(" ", "");
        if (clean.Length == 11)
        {
            return $"{clean[..2]} {clean[2..5]} {clean[5..8]} {clean[8..]}";
        }
        return abn;
    }

    private string GetEmailStyles() => @"
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #374151; margin: 0; padding: 0; background-color: #f3f4f6; }
        .container { max-width: 600px; margin: 0 auto; background-color: #ffffff; }
        .header { background-color: #2563eb; color: white; padding: 32px 24px; text-align: center; }
        .header h1 { margin: 0; font-size: 24px; font-weight: 600; }
        .content { padding: 32px 24px; }
        .info-box { background-color: #f9fafb; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px; margin: 16px 0; }
        .info-row { margin: 8px 0; }
        .label { color: #6b7280; font-size: 14px; }
        .value { color: #111827; font-weight: 500; }
        .button { display: inline-block; background-color: #2563eb; color: white; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: 500; margin: 16px 0; }
        .footer { background-color: #f9fafb; padding: 24px; text-align: center; font-size: 12px; color: #6b7280; border-top: 1px solid #e5e7eb; }
        .highlight { background-color: #dbeafe; border-left: 4px solid #2563eb; padding: 12px 16px; margin: 16px 0; }
        .warning { background-color: #fef3c7; border-left: 4px solid #f59e0b; padding: 12px 16px; margin: 16px 0; }
    ";

    private string GetLeadCapturedHtml(Lead lead) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>{GetEmailStyles()}</style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>Thank You for Using Renewtron</h1>
        </div>
        <div class=""content"">
            <p>Hi {lead.FullName},</p>
            <p>Thank you for using Renewtron for your business name renewal needs.</p>

            <div class=""info-box"">
                <div class=""info-row"">
                    <span class=""label"">ABN:</span>
                    <span class=""value"">{FormatAbn(lead.Abn)}</span>
                </div>
                <div class=""info-row"">
                    <span class=""label"">Date:</span>
                    <span class=""value"">{lead.CreatedAt:MMMM dd, yyyy}</span>
                </div>
            </div>

            <p>We've saved your details and will be here whenever you need to renew your business name.</p>

            <p>If you have any questions, please don't hesitate to contact us.</p>
        </div>
        <div class=""footer"">
            <p>This is an automated email from Renewtron.</p>
            <p>If you have any questions, please contact our support team.</p>
        </div>
    </div>
</body>
</html>";

    private string GetLeadCapturedPlain(Lead lead) => $@"
Thank You for Using Renewtron

Hi {lead.FullName},

Thank you for using Renewtron for your business name renewal needs.

ABN: {FormatAbn(lead.Abn)}
Date: {lead.CreatedAt:MMMM dd, yyyy}

We've saved your details and will be here whenever you need to renew your business name.

If you have any questions, please don't hesitate to contact us.

---
This is an automated email from Renewtron.
";

    private string GetNotDueForRenewalHtml(Lead lead) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>{GetEmailStyles()}</style>
</head>
<body>
    <div class=""container"">
        <div class=""header"" style=""background-color: #f59e0b;"">
            <h1>Your Business Name Registration Status</h1>
        </div>
        <div class=""content"">
            <p>Hi {lead.FullName},</p>
            <p>Thank you for checking your business name renewal status with Renewtron.</p>

            <div class=""warning"">
                <strong>Not Due for Renewal Yet</strong>
                <p style=""margin: 8px 0 0 0;"">Your business name is currently active and not due for renewal at this time.</p>
            </div>

            <div class=""info-box"">
                <div class=""info-row"">
                    <span class=""label"">ABN:</span>
                    <span class=""value"">{FormatAbn(lead.Abn)}</span>
                </div>
            </div>

            <p><strong>What does this mean?</strong></p>
            <ul>
                <li>Your business name registration is still valid</li>
                <li>No action is required from you at this time</li>
                <li>ASIC will notify you when your renewal period approaches</li>
            </ul>

            {(lead.ReminderOptIn ? @"<div class=""highlight"">
                <strong>Reminder Set!</strong>
                <p style=""margin: 8px 0 0 0;"">We'll send you an email when your business name is due for renewal.</p>
            </div>" : "")}

            <p>When your business name becomes due for renewal, you can return to Renewtron and we'll process it quickly and easily.</p>
        </div>
        <div class=""footer"">
            <p>This is an automated email from Renewtron.</p>
            <p>If you have any questions, please contact our support team.</p>
        </div>
    </div>
</body>
</html>";

    private string GetNotDueForRenewalPlain(Lead lead) => $@"
Your Business Name Registration Status

Hi {lead.FullName},

Thank you for checking your business name renewal status with Renewtron.

NOT DUE FOR RENEWAL YET
Your business name is currently active and not due for renewal at this time.

ABN: {FormatAbn(lead.Abn)}

What does this mean?
- Your business name registration is still valid
- No action is required from you at this time
- ASIC will notify you when your renewal period approaches

{(lead.ReminderOptIn ? "REMINDER SET: We'll send you an email when your business name is due for renewal.\n" : "")}
When your business name becomes due for renewal, you can return to Renewtron and we'll process it quickly and easily.

---
This is an automated email from Renewtron.
";

    private string GetNoBusinessNamesHtml(Lead lead) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>{GetEmailStyles()}</style>
</head>
<body>
    <div class=""container"">
        <div class=""header"" style=""background-color: #6b7280;"">
            <h1>ABN Search Results</h1>
        </div>
        <div class=""content"">
            <p>Hi {lead.FullName},</p>
            <p>Thank you for using Renewtron to check your business name status.</p>

            <div class=""info-box"">
                <strong>No Business Names Found</strong>
                <p style=""margin: 8px 0 0 0;"">We couldn't find any business names registered under the ABN you provided.</p>
            </div>

            <div class=""info-box"">
                <div class=""info-row"">
                    <span class=""label"">ABN Searched:</span>
                    <span class=""value"">{FormatAbn(lead.Abn)}</span>
                </div>
            </div>

            <p><strong>This could mean:</strong></p>
            <ul>
                <li>The ABN doesn't have a registered business name</li>
                <li>The business name has been cancelled or deregistered</li>
                <li>There may be a typo in the ABN number</li>
            </ul>

            <p><strong>Need to register a business name?</strong></p>
            <p>You can register a new business name through ASIC at:</p>
            <p><a href=""https://asic.gov.au/for-business/registering-a-business-name/"" class=""button"">Register a Business Name</a></p>
        </div>
        <div class=""footer"">
            <p>This is an automated email from Renewtron.</p>
            <p>If you have any questions, please contact our support team.</p>
        </div>
    </div>
</body>
</html>";

    private string GetNoBusinessNamesPlain(Lead lead) => $@"
ABN Search Results

Hi {lead.FullName},

Thank you for using Renewtron to check your business name status.

NO BUSINESS NAMES FOUND
We couldn't find any business names registered under the ABN you provided.

ABN Searched: {FormatAbn(lead.Abn)}

This could mean:
- The ABN doesn't have a registered business name
- The business name has been cancelled or deregistered
- There may be a typo in the ABN number

Need to register a business name?
You can register a new business name through ASIC at:
https://asic.gov.au/for-business/registering-a-business-name/

---
This is an automated email from Renewtron.
";

    private string GetRenewalInProgressHtml(Lead lead) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>{GetEmailStyles()}</style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>Your Business Name Renewal Status</h1>
        </div>
        <div class=""content"">
            <p>Hi {lead.FullName},</p>
            <p>Thank you for checking your business name renewal status with Renewtron.</p>

            <div class=""highlight"">
                <strong>Renewal Already in Progress</strong>
                <p style=""margin: 8px 0 0 0;"">A renewal for this ABN is already being processed. This could be because you or someone else has recently initiated a renewal.</p>
            </div>

            <div class=""info-box"">
                <div class=""info-row"">
                    <span class=""label"">ABN:</span>
                    <span class=""value"">{FormatAbn(lead.Abn)}</span>
                </div>
            </div>

            <p><strong>What should you do?</strong></p>
            <ul>
                <li>Wait for the current renewal to complete</li>
                <li>Check your email for any confirmation messages</li>
                <li>If you didn't initiate a renewal, please contact us</li>
            </ul>

            <p>If you have any concerns or questions about this renewal, please don't hesitate to contact us.</p>
        </div>
        <div class=""footer"">
            <p>This is an automated email from Renewtron.</p>
            <p>If you have any questions, please contact our support team.</p>
        </div>
    </div>
</body>
</html>";

    private string GetRenewalInProgressPlain(Lead lead) => $@"
Your Business Name Renewal Status

Hi {lead.FullName},

Thank you for checking your business name renewal status with Renewtron.

RENEWAL ALREADY IN PROGRESS
A renewal for this ABN is already being processed. This could be because you or someone else has recently initiated a renewal.

ABN: {FormatAbn(lead.Abn)}

What should you do?
- Wait for the current renewal to complete
- Check your email for any confirmation messages
- If you didn't initiate a renewal, please contact us

If you have any concerns or questions about this renewal, please don't hesitate to contact us.

---
This is an automated email from Renewtron.
";
}
