namespace Asic.Client.Models;

public class PaymentResult
{
    public bool Success { get; private set; }
    public string HostedTokenizationId { get; private set; }
    public string Message { get; private set; }

    public static PaymentResult Succeeded(string hostedTokenizationId)
    {
        return new PaymentResult
        {
            Success = true,
            HostedTokenizationId = hostedTokenizationId,
            Message = "Payment processed successfully"
        };
    }

    public static PaymentResult Failed(string message)
    {
        return new PaymentResult
        {
            Success = false,
            Message = message
        };
    }
}