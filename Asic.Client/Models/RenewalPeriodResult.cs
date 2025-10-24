namespace Asic.Client;

internal class RenewalPeriodResult
{
    public bool Success { get; set; }
    public string TransactionReference { get; set; }
    public string Content { get; set; }
    public bool JumpedToEmail { get; set; }
    public string Message { get; set; }

    public static RenewalPeriodResult Succeeded(string transactionRef, string content, bool jumpedToEmail)
    {
        return new RenewalPeriodResult
        {
            Success = true,
            TransactionReference = transactionRef,
            Content = content,
            JumpedToEmail = jumpedToEmail,
            Message = "Success"
        };
    }

    public static RenewalPeriodResult Failed(string message)
    {
        return new RenewalPeriodResult
        {
            Success = false,
            Message = message,
            JumpedToEmail = false
        };
    }
}