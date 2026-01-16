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

    // ThreeDS preparation
    public string ThreeDSServerTransactionId { get; set; }

    // Tokenization result
    public string HostedTokenizationId { get; set; }

    // 3DS flow data
    public string ThreeDSRedirectUrl { get; set; }
    public string ThreeDSIframeUrl { get; set; }
    public string ThreeDSFormAction { get; set; }
    public string ThreeDSMethodData { get; set; }
    public string ThreeDSCallbackUrl { get; set; }
    public string ThreeDSCallbackData { get; set; }
    public string ThreeDSResultUrl { get; set; }
    public bool ThreeDSComplete { get; set; }

    // 3DS Challenge data
    public string ThreeDSChallengeResponseContent { get; set; }
    public string ThreeDSChallengeUrl { get; set; }
    public string ThreeDSSessionData { get; set; }
    public string ThreeDSCReq { get; set; }
    public string ThreeDSIssuerId { get; set; }
    public string ThreeDSTransactionId { get; set; }
    public bool ThreeDSRequiresOtp { get; set; }

    // SecureSuite 3DS Method data
    public string ThreeDSMethodUrl { get; set; }
    public string ThreeDSMethodNotificationUrl { get; set; }

    // RSA 3DS data (NAB) - Legacy
    public string ThreeDSAcsTransId { get; set; }
    public string ThreeDSMessageVersion { get; set; }
    public string ThreeDSChallengeWindowSize { get; set; }

    // Cardinal Commerce 3DS data (New)
    public string CardinalOid { get; set; }
    public string CardinalTid { get; set; }
    public string CardinalStepupRequestId { get; set; }
    public string CardinalCredentialId { get; set; }
    public string CardinalGroupId { get; set; }
    public string CardinalLanguageCode { get; set; }

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