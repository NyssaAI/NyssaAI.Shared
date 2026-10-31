using NyssaAI.Configuration.Client;
using NyssaAI.Results;

namespace NyssaAI.Configuration;

/// <summary>
/// Acquires and renews OAuth 2.0 credentials through an interactive browser flow.
/// </summary>
/// <remarks>
/// All three operations open a browser through the interactive OAuth flow. None of them is ever reached from
/// <see cref="IConfigurationReader"/>: a read that needs sign-in throws, and the
/// application decides where and when a person can act on it.
/// </remarks>
public interface IConfigurationOAuthWriter
{
    /// <summary>
    /// Acquires an OAuth credential for a service the application talks to and stores it at
    /// <paramref name="namespace"/>/<paramref name="key"/>. The server description is sealed with
    /// the credential, so later reads refresh it without the application's help.
    /// </summary>
    /// <param name="namespace">The namespace to hold the credential.</param>
    /// <param name="key">The key of the credential within the namespace.</param>
    /// <param name="options">The authorization server, client id, and scopes to sign in with.</param>
    /// <param name="cancellationToken">Token used to cancel the flow.</param>
    ValueTask<Result> AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires the credential a provider named in the manifest authenticates with. The provider's
    /// package supplies the server; the manifest supplies where the credential is stored.
    /// </summary>
    /// <param name="provider">The provider name, as the manifest and stored entries record it.</param>
    /// <param name="cancellationToken">Token used to cancel the flow.</param>
    ValueTask<Result> AuthorizeProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs sign-in for an existing credential whose refresh token the server has rejected,
    /// using the server that credential already records.
    /// </summary>
    /// <param name="namespace">The namespace holding the credential.</param>
    /// <param name="key">The key of the credential within the namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the flow.</param>
    ValueTask<Result> ReauthorizeAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);
}
