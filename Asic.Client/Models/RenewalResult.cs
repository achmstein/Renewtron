namespace Asic.Client.Models;

public class RenewalResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public string TransactionReference { get; set; }
    public string HostedTokenizationId { get; set; }
    public string? FailedAtStep { get; set; }

    // Set when the renewal was lodged with ASIC's "Pay later -> BPAY": it isn't paid until
    // someone pays these at their bank, and ASIC shows them only once.
    public string? BpayBillerCode { get; set; }
    public string? BpayReference { get; set; }

    public static RenewalResult Success(string transactionReference, string hostedTokenizationId)
    {
        return new RenewalResult
        {
            IsSuccess = true,
            Message = "Renewal completed successfully",
            TransactionReference = transactionReference,
            HostedTokenizationId = hostedTokenizationId
        };
    }

    public static RenewalResult Failed(string message, string? failedAtStep = null)
    {
        return new RenewalResult
        {
            IsSuccess = false,
            Message = message,
            FailedAtStep = failedAtStep
        };
    }
}
