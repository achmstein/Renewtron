using Asic.Integrations.Shared;

namespace Asic.Integrations.Renewal;

internal static class RenewalStepResults
{
    /// <summary>The renewal's outcome: <paramref name="onSuccess"/>'s, or the failing step's message and name.</summary>
    public static async Task<RenewalResult> ToRenewalResultAsync<T>(this Task<StepResult<T>> resultTask, Func<T, RenewalResult> onSuccess) where T : class
    {
        var result = await resultTask;
        return result.IsSuccess ? onSuccess(result.Value) : RenewalResult.Failed(result.ErrorMessage, result.StepName);
    }
}
