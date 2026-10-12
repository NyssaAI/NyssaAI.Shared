namespace NyssaAI.Results;

/// <summary>ResultExtensions unwraps asynchronous results at an explicit exception boundary.</summary>
public static class ResultExtensions
{
    /// <summary>Awaits a result without blocking and returns its value, or throws its structured errors.</summary>
    /// <typeparam name="TPayload">The successful payload type.</typeparam>
    /// <param name="resultTask">The asynchronous result to await.</param>
    /// <returns>The original successful payload, including a successful null.</returns>
    /// <remarks>Exceptions and cancellation from the task propagate unchanged.</remarks>
    /// <exception cref="ResultException">The completed result is a failure.</exception>
    public static async Task<TPayload> UnwrapAsync<TPayload>(this Task<Result<TPayload>> resultTask) =>
        (await resultTask.ConfigureAwait(false)).Unwrap();
}
