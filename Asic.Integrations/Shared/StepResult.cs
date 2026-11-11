using System.Diagnostics.CodeAnalysis;

namespace Asic.Integrations.Shared;

/// <summary>
/// One step of a multi-step flow: the flow's state on success, or why and where it stopped.
/// Steps chain with <see cref="StepResultExtensions.ThenAsync{T, TNext}"/>; the first failure
/// skips the rest and is carried to the end unchanged.
/// </summary>
public sealed class StepResult<T> where T : class
{
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(ErrorMessage), nameof(StepName))]
    public bool IsSuccess { get; }

    public T? Value { get; }
    public string? ErrorMessage { get; }

    /// <summary>The step that failed, as named in its own <see cref="Failure"/>.</summary>
    public string? StepName { get; }

    private StepResult(bool success, T? value, string? errorMessage, string? stepName)
    {
        IsSuccess = success;
        Value = value;
        ErrorMessage = errorMessage;
        StepName = stepName;
    }

    public static StepResult<T> Success(T value) => new(true, value, null, null);

    public static StepResult<T> Failure(string errorMessage, string stepName) => new(false, null, errorMessage, stepName);

    /// <summary>Runs <paramref name="next"/> after a success; a failure passes through untouched.</summary>
    /// <param name="stepName">The step's name, for reading the chain: a failing step reports its own name.</param>
    public async Task<StepResult<TNext>> ThenAsync<TNext>(string stepName, Func<T, Task<StepResult<TNext>>> next) where TNext : class =>
        IsSuccess ? await next(Value) : StepResult<TNext>.Failure(ErrorMessage, StepName);
}

public static class StepResultExtensions
{
    /// <inheritdoc cref="StepResult{T}.ThenAsync{TNext}"/>
    public static async Task<StepResult<TNext>> ThenAsync<T, TNext>(this Task<StepResult<T>> resultTask, string stepName,
        Func<T, Task<StepResult<TNext>>> next) where T : class where TNext : class =>
        await (await resultTask).ThenAsync(stepName, next);
}
