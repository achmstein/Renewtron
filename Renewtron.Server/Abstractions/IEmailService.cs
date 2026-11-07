using System.Net.Mail;

namespace Renewtron.Abstractions;

public interface IEmailService
{
    Task SendRenewalConfirmationAsync(
        string toEmail,
        string businessName,
        string abn,
        int renewalYears,
        decimal amountPaid,
        string transactionReference);

    /// <summary>
    /// After paying for names ASIC isn't taking yet: payment received, ASIC only opens a renewal
    /// in the month before it's due, and we'll lodge it then without them doing anything. This is
    /// where the customer learns it — the renewal site doesn't say (David, 7 Nov 2026).
    /// </summary>
    Task SendPaidEarlyAsync(string toEmail, string abn, string[] businessNames, int renewalYears, decimal amountPaid);
}
