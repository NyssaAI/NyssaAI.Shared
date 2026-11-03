using NyssaAI.Configuration.Client;
using NyssaAI.Results;

namespace NyssaAI.Configuration;

/// <summary>
/// Captures secrets out-of-band directly from the user without exposing plaintext credentials to LLM callers.
/// </summary>
public interface IConfigurationSecretWriter
{
    /// <summary>
    /// Interactively prompts the user out-of-band for a secret, stores it into local encryption ("aesLocal")
    /// or a configured cloud vault, and returns only a sanitized metadata receipt.
    /// </summary>
    /// <param name="namespace">The namespace to hold the credential.</param>
    /// <param name="key">The key of the credential within the namespace.</param>
    /// <param name="options">Optional prompt configuration, timeout, and provider overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A sanitized receipt containing storage metadata, or a failure result.</returns>
    ValueTask<Result<SecretReceipt>> CaptureSecretAsync(
        string @namespace,
        string key,
        CaptureSecretOptions? options = null,
        CancellationToken cancellationToken = default);
}
