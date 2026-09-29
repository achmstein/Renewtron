using SendGrid;
using SendGrid.Helpers.Mail;

namespace Renewtron.Services;

/// <summary>
/// Sends through SendGrid and treats a non-2xx response as a failure. The SendGrid
/// client doesn't throw on a rejected request — a revoked API key comes back as a 401
/// that was silently ignored, so emails stopped with no trace. Failures are logged and
/// thrown; every caller already catches, so a completed ASIC renewal never fails over
/// an email, and the win-back audit row is marked Failed instead of Sent.
/// </summary>
public static class SendGridDelivery
{
    public static async Task SendAsync(
        SendGridClient client, SendGridMessage message, string apiKey, string kind, string toEmail, ILogger logger)
    {
        var domain = RecipientDomain(toEmail);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("Email not sent ({Kind} to @{Domain}): the SendGrid API key is not configured", kind, domain);
            throw new InvalidOperationException("SendGrid API key is not configured.");
        }

        var response = await client.SendEmailAsync(message);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Body.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500];
        logger.LogError(
            "Email not sent ({Kind} to @{Domain}): SendGrid returned {StatusCode}: {Body}",
            kind, domain, (int)response.StatusCode, body);
        throw new InvalidOperationException($"SendGrid returned {(int)response.StatusCode}.");
    }

    // The domain is enough to diagnose delivery; the full address stays out of the logs.
    private static string RecipientDomain(string email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at >= 0 ? email![(at + 1)..] : "unknown";
    }
}
