using Microsoft.Extensions.Options;
using Renewtron.Abstractions;
using Renewtron.Settings;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Renewtron.Services;

public class EmailService : IEmailService
{
    private readonly SendGridClient _client;
    private readonly SendGridSettings _settings;
    private readonly PortalSettings _portal;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptionsSnapshot<SendGridSettings> settings,
        IOptionsSnapshot<PortalSettings> portal,
        ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _portal = portal.Value;
        _logger = logger;
        _client = new SendGridClient(_settings.ApiKey);
    }

    public async Task SendRenewalConfirmationAsync(
        string toEmail,
        string businessName,
        string abn,
        int renewalYears,
        decimal amountPaid,
        string transactionReference)
    {
        var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
        var to = new EmailAddress(toEmail);
        var subject = $"Business Name Renewal Confirmation - {EmailText.SubjectSafe(businessName)}";

        // Values below come from customers and ASIC — encode everything that lands in HTML.
        static string h(string? v) => EmailText.Html(v);

        // Customers keep an account in the Business Portal: their names, renewal history and
        // ASIC keys. With a signing key the button signs them straight in (once, for 72 hours);
        // otherwise it opens the sign-in page with their email filled in.
        var signInUrl = _portal.SignInUrl(toEmail, DateTimeOffset.UtcNow);
        var loginUrl = _portal.LoginUrl(null);
        var portalUrl = signInUrl ?? _portal.LoginUrl(toEmail);
        var portalLabel = signInUrl is null ? "View your business names in your Business Portal" : "Open your Business Portal";
        var portalNote = signInUrl is null ? "" : $@"
            <p style='margin-top: 8px; text-align: center; font-size: 12px; color: #6B7280;'>
                This link signs you in and works once, for 72 hours. After that, sign in with your email at {h(loginUrl)}.
            </p>";
        var portalHtml = portalUrl is null ? "" : $@"
            <p style='margin-top: 24px; text-align: center;'>
                <a href='{h(portalUrl)}' style='background-color: #4F46E5; color: #ffffff; padding: 12px 22px; border-radius: 6px; text-decoration: none; display: inline-block; font-weight: bold;'>{portalLabel}</a>
            </p>{portalNote}";
        var portalText = portalUrl is null ? "" : signInUrl is null
            ? $"\nView your business names in your Business Portal: {portalUrl}\n"
            : $"\nOpen your Business Portal: {portalUrl}\nThis link signs you in and works once, for 72 hours. After that, sign in with your email at {loginUrl}.\n";

        var htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4F46E5; color: white; padding: 20px; text-align: center; }}
        .content {{ background-color: #f9fafb; padding: 30px; }}
        .detail-row {{ margin: 10px 0; padding: 10px; background-color: white; border-radius: 5px; }}
        .label {{ font-weight: bold; color: #6B7280; }}
        .value {{ color: #111827; }}
        .footer {{ text-align: center; padding: 20px; color: #6B7280; font-size: 12px; }}
        .success-badge {{ background-color: #10B981; color: white; padding: 5px 15px; border-radius: 20px; display: inline-block; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✓ Renewal Successful</h1>
        </div>
        <div class='content'>
            <p>Dear Valued Customer,</p>
            <p>Your business name has been successfully renewed with ASIC.</p>
            
            <div class='detail-row'>
                <div class='label'>Business Name:</div>
                <div class='value'>{h(businessName)}</div>
            </div>
            
            <div class='detail-row'>
                <div class='label'>ABN:</div>
                <div class='value'>{h(abn)}</div>
            </div>
            
            <div class='detail-row'>
                <div class='label'>Renewal Period:</div>
                <div class='value'>{renewalYears} {(renewalYears == 1 ? "Year" : "Years")}</div>
            </div>
            
            <div class='detail-row'>
                <div class='label'>Amount Paid:</div>
                <div class='value'>${amountPaid:F2}</div>
            </div>
            
            <div class='detail-row'>
                <div class='label'>Transaction Reference:</div>
                <div class='value'>{h(transactionReference)}</div>
            </div>
            
            <div class='detail-row'>
                <div class='label'>Date:</div>
                <div class='value'>{DateTime.UtcNow:MMMM dd, yyyy 'at' hh:mm tt} UTC</div>
            </div>
            
            <p style='margin-top: 30px;'>
                <strong>What's Next?</strong><br>
                Your business name registration has been extended and is now active with ASIC. 
                Please keep this email for your records.
            </p>{portalHtml}
        </div>
        <div class='footer'>
            <p>This is an automated email from Renewtron. Please do not reply to this email.</p>
            <p>If you have any questions, please contact our support team.</p>
        </div>
    </div>
</body>
</html>";

        var plainTextContent = $@"
Business Name Renewal Confirmation

Your business name has been successfully renewed with ASIC.

Business Name: {businessName}
ABN: {abn}
Renewal Period: {renewalYears} {(renewalYears == 1 ? "Year" : "Years")}
Amount Paid: ${amountPaid:F2}
Transaction Reference: {transactionReference}
Date: {DateTime.UtcNow:MMMM dd, yyyy 'at' hh:mm tt} UTC

Please keep this email for your records.
{portalText}
---
This is an automated email from Renewtron.
";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
        await SendGridDelivery.SendAsync(_client, msg, _settings.ApiKey, "renewal confirmation", toEmail, _logger);
    }
}