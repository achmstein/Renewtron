using Asic.Integrations.Payment;

namespace Asic.Integrations.Renewal;

/// <summary>
/// The renewal's state as it moves through its steps: the inputs, then whatever each step
/// learns from ASIC's renewal pages.
/// </summary>
public class RenewalStepData
{
    // Input
    public required string Abn { get; set; }
    public required string AccountNumber { get; set; }
    public int RenewalYears { get; set; }
    public required string Email { get; set; }
    public required CreditCardDetails CardDetails { get; set; }

    // ADF session
    public string? SessionId { get; set; }
    public string? AdfWindowId { get; set; }
    public string? ViewState { get; set; }

    /// <summary>The chosen name's radio id (form-encoded).</summary>
    public string? SelectedBusinessNameId { get; set; }

    /// <summary>The last page ASIC returned; the next step reads its region ids from it.</summary>
    public string? PageContent { get; set; }

    /// <summary>ASIC resumed an earlier session and jumped straight to the email page.</summary>
    public bool JumpedToEmail { get; set; }

    /// <summary>The renewal period radio's value: "0" one year, "1" three years.</summary>
    public string? PeriodValue { get; set; }

    public string? TransactionReference { get; set; }
    public string? PaymentUrl { get; set; }
    public string? HostedTokenizationId { get; set; }

    // Pay later by BPAY
    public string? BpayBillerCode { get; set; }
    public string? BpayReference { get; set; }

    public static RenewalStepData Create(string abn, string accountNumber, int renewalYears, string email, CreditCardDetails cardDetails) => new()
    {
        Abn = abn,
        AccountNumber = accountNumber,
        RenewalYears = renewalYears,
        Email = email,
        CardDetails = cardDetails,
    };
}
