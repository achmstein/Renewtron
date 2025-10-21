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
}
