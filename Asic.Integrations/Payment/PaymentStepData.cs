namespace Asic.Integrations.Payment;

/// <summary>
/// The payment's state as it moves through its steps: the inputs, then whatever each step
/// learns from ASIC's payment page, the card gateway and the issuer's 3DS challenge.
/// </summary>
public class PaymentStepData
{
    // Input
    public required string PaymentUrl { get; set; }
    /// <summary>ASIC's transaction reference: unknown when ASIC resumed a session and skipped the page that shows it.</summary>
    public string? SessionId { get; set; }
    public required CreditCardDetails CardDetails { get; set; }

    // ASIC's payment page
    public string? AdfWindowId { get; set; }
    public string? ViewState { get; set; }
    public string? TokenizationFormUrl { get; set; }

    // Card gateway
    public string? BrandName { get; set; }
    public string? ThreeDSServerTransactionId { get; set; }
    public string? HostedTokenizationId { get; set; }

    // 3DS
    public string? ThreeDSRedirectUrl { get; set; }
    public bool ThreeDSComplete { get; set; }
    public string? ThreeDSChallengeResponseContent { get; set; }
    public string? ThreeDSChallengeUrl { get; set; }
    public string? ThreeDSSessionData { get; set; }
    public string? ThreeDSCReq { get; set; }
    public string? ThreeDSIssuerId { get; set; }
    public string? ThreeDSTransactionId { get; set; }
    public bool ThreeDSRequiresOtp { get; set; }
    public string? ThreeDSAcsTransId { get; set; }
    public string? ThreeDSMessageVersion { get; set; }
    public string? ThreeDSChallengeWindowSize { get; set; }

    // Cardinal Commerce 3DS
    public string? CardinalOid { get; set; }
    public string? CardinalTid { get; set; }
    public string? CardinalStepupRequestId { get; set; }

    public static PaymentStepData Create(string paymentUrl, string? sessionId, CreditCardDetails cardDetails) => new()
    {
        PaymentUrl = paymentUrl,
        SessionId = sessionId,
        CardDetails = cardDetails,
    };
}
