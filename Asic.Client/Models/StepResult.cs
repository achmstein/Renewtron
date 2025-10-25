namespace Asic.Client.Models;

/// <summary>
/// Represents the result of a single step in a multi-step process.
/// Supports railway-oriented programming pattern for chaining operations.
/// </summary>
public class StepResult<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public string ErrorMessage { get; }
    public string StepName { get; }

    private StepResult(bool success, T value, string errorMessage, string stepName)
    {
        IsSuccess = success;
        Value = value;
        ErrorMessage = errorMessage;
        StepName = stepName;
    }

    public static StepResult<T> Success(T value) =>
        new StepResult<T>(true, value, null, null);

    public static StepResult<T> Failure(string errorMessage, string stepName) =>
        new StepResult<T>(false, default, errorMessage, stepName);

    /// <summary>
    /// Chains the next async operation. Only executes if the current step succeeded.
    /// </summary>
    public async Task<StepResult<TNext>> ThenAsync<TNext>(
        string stepName,
        Func<T, Task<StepResult<TNext>>> next)
    {
        if (!IsSuccess)
            return StepResult<TNext>.Failure(ErrorMessage, StepName);

        return await next(Value);
    }

    /// <summary>
    /// Chains the next synchronous operation. Only executes if the current step succeeded.
    /// </summary>
    public StepResult<TNext> Then<TNext>(
        string stepName,
        Func<T, StepResult<TNext>> next)
    {
        if (!IsSuccess)
            return StepResult<TNext>.Failure(ErrorMessage, StepName);

        return next(Value);
    }

    /// <summary>
    /// Converts the step result to a RenewalResult.
    /// </summary>
    public RenewalResult ToRenewalResult(Func<T, RenewalResult> onSuccess)
    {
        return IsSuccess
            ? onSuccess(Value)
            : RenewalResult.Failed(ErrorMessage, StepName);
    }
}

/// <summary>
/// Extension methods for Task&lt;StepResult&lt;T&gt;&gt; to enable fluent async chaining.
/// </summary>
public static class StepResultExtensions
{
    /// <summary>
    /// Chains the next async operation on a Task&lt;StepResult&lt;T&gt;&gt;.
    /// Only executes if the previous step succeeded.
    /// </summary>
    public static async Task<StepResult<TNext>> ThenAsync<T, TNext>(
        this Task<StepResult<T>> resultTask,
        string stepName,
        Func<T, Task<StepResult<TNext>>> next)
    {
        var result = await resultTask;
        return await result.ThenAsync(stepName, next);
    }

    /// <summary>
    /// Converts a Task&lt;StepResult&lt;T&gt;&gt; to a RenewalResult.
    /// </summary>
    public static async Task<RenewalResult> ToRenewalResultAsync<T>(
        this Task<StepResult<T>> resultTask,
        Func<T, RenewalResult> onSuccess)
    {
        var result = await resultTask;
        return result.ToRenewalResult(onSuccess);
    }
}
