using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>Keeps a failed Result and its operation receipt together without converting failure into success.</summary>
internal sealed record ConfigurationAttempt
{
    public required Result Result { get; init; }
    public ConfigurationOperationProgress? Progress { get; init; }
    public ConfigurationRecoveryHandle? RecoveryHandle { get; init; }
    public bool IsFailure => Result.IsFailure;
    public bool IsSuccess => Result.IsSuccess;
    public IReadOnlyList<Error> Errors => Result.Errors;
    public void ThrowIfError() => Result.ThrowIfError();
    public static implicit operator ConfigurationAttempt(Result result) => new() { Result = result };
    public static async ValueTask<ConfigurationAttempt> FromAsync(ValueTask<Result> result) => await result.ConfigureAwait(false);
}
