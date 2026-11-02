using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>Keeps a typed outcome and its partial progress together.</summary>
internal sealed record ConfigurationAttempt<TValue>
{
    public required Result<TValue> Result { get; init; }
    public ConfigurationOperationProgress? Progress { get; init; }
    public ConfigurationRecoveryHandle? RecoveryHandle { get; init; }
    public bool IsFailure => Result.IsFailure;
    public bool IsSuccess => Result.IsSuccess;
    public TValue Value => Result.Value;
    public IReadOnlyList<Error> Errors => Result.Errors;
    public TValue ThrowIfError() => Result.ThrowIfError();
    public static implicit operator ConfigurationAttempt<TValue>(Result<TValue> result) => new() { Result = result };
}
