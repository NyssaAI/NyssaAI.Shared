using NyssaAI.Configuration.Client;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IReauthorizableProvider is the seam for the one provider whose credentials are obtained from a
/// person rather than written by the app: it can acquire a credential for an address interactively,
/// and re-acquire one whose refresh token the server has rejected.
/// </summary>
/// <remarks>
/// Separate from <see cref="IConfigurationProvider"/> because resolving an entry is something every
/// provider does on every read, while opening a browser is something one provider does rarely. Both
/// operations return the entry to store rather than storing it, so the store writes it under the
/// same document lock every other entry change runs under.
/// </remarks>
internal interface IReauthorizableProvider
{
    /// <summary>Runs the interactive flow against the given server and returns the entry that records the credential.</summary>
    ValueTask<Result<ConfigurationValue>> AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the interactive flow for an existing entry, using the server that entry already
    /// records, and returns the replacement entry with its re-authorization flag cleared.
    /// </summary>
    ValueTask<Result<ConfigurationValue>> ReauthorizeAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);
}
