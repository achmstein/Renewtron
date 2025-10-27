namespace Asic.Client.Models;

/// <summary>
/// Holds the accumulated state as the payment process progresses through steps.
/// Each step can read from and add to this data.
/// </summary>
public class PaymentStepData
{
    // Input parameters
    public string PaymentUrl { get; set; }
    public string SessionId { get; set; }
    public string AdfWindowId { get; set; }
    public CreditCardDetails CardDetails { get; set; }

    // Payment gateway data
    public string TokenizationFormUrl { get; set; }
    public string ViewState { get; set; }

    // Card information
    public string BrandName { get; set; }

    // Tokenization result
    public string HostedTokenizationId { get; set; }
    public object ThreeDSServerTransactionId { get; internal set; }

    public static PaymentStepData Create(string paymentUrl, string sessionId, CreditCardDetails cardDetails)
    {
        return new PaymentStepData
        {
            PaymentUrl = paymentUrl,
            SessionId = sessionId,
            CardDetails = cardDetails
        };
    }
}
