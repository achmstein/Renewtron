namespace Asic.Client.Models;

/// <summary>
/// Holds the accumulated state as the renewal process progresses through steps.
/// Each step can read from and add to this data.
/// </summary>
public class RenewalStepData
{
    // Input parameters
    public string Abn { get; set; }
    public string AccountNumber { get; set; }
    public int RenewalYears { get; set; }
    public string Email { get; set; }
    public CreditCardDetails CardDetails { get; set; }

    // Session data
    public string SessionId { get; set; }
    public string AdfWindowId { get; set; }
    public string ViewState { get; set; }

    // Page content from various steps
    public string PageContent { get; set; }

    // Flow control
    public bool JumpedToEmail { get; set; }

    // Transaction data
    public string TransactionReference { get; set; }

    // Payment data
    public string PaymentUrl { get; set; }
    public string HostedTokenizationId { get; set; }

    public static RenewalStepData Create(string abn, string accountNumber, int renewalYears, string email, CreditCardDetails cardDetails)
    {
        return new RenewalStepData
        {
            Abn = abn,
            AccountNumber = accountNumber,
            RenewalYears = renewalYears,
            Email = email,
            CardDetails = cardDetails
        };
    }
}
