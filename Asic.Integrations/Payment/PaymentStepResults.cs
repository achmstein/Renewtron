using Asic.Integrations.Shared;

namespace Asic.Integrations.Payment;

internal static class PaymentStepResults
{
    /// <summary>The payment's outcome: <paramref name="onSuccess"/>'s, or the failing step's message (its name isn't reported).</summary>
    public static async Task<PaymentResult> ToPaymentResultAsync<T>(this Task<StepResult<T>> resultTask, Func<T, PaymentResult> onSuccess) where T : class
    {
        var result = await resultTask;
        return result.IsSuccess ? onSuccess(result.Value) : PaymentResult.Failed(result.ErrorMessage);
    }
}
