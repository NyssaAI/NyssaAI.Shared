using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IAccessTokenSource hands a provider package the bearer token it signs requests with. The
/// library binds one to the credential entry the manifest names for that provider, and refresh,
/// rotation, locking, and re-authorization all happen behind this one call.
/// </summary>
/// <remarks>
/// A vault package puts this behind a <c>DelegatingHandler</c> that sets the Authorization header
/// and never sees a refresh token, a cipher, or a key. A vault that authenticates some other way
/// simply does not take one.
/// </remarks>
public interface IAccessTokenSource
{
    /// <summary>Returns a currently valid access token, refreshing the stored credential if it is stale.</summary>
    /// <param name="cancellationToken">Token used to cancel the read or refresh.</param>
    /// <returns>
    /// The access token, or a failure: authorization required when no credential has been acquired
    /// yet, re-authorization required when the server rejected the refresh token, or circular
    /// credential when the entry would have to be read through the provider asking for it.
    /// </returns>
    ValueTask<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
