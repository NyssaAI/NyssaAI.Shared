using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// Abstraction for capturing a secret out-of-band directly from the user.
/// </summary>
public interface ISecretInputProvider
{
    /// <summary>
    /// Executes the out-of-band capture prompt.
    /// </summary>
    /// <param name="request">Prompt parameters, available providers, and confirmation settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured secret in an ephemeral disposable buffer, or a failure result.</returns>
    ValueTask<Result<CapturedSecret>> CaptureAsync(
        SecretPromptRequest request,
        CancellationToken cancellationToken = default);
}
