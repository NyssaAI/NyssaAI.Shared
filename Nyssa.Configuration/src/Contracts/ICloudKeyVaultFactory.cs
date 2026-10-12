using NyssaAI.Configuration.Client;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// ICloudKeyVaultFactory is what a vault package registers with the container. The
/// administrator's manifest names a vault by provider name and <c>kind</c>; the library finds the
/// factory whose <see cref="Kind"/> matches and asks it to build the vault from the manifest entry.
/// </summary>
/// <remarks>
/// Register one with <c>services.AddSingleton&lt;ICloudKeyVaultFactory, ContosoVaultFactory&gt;()</c>.
/// Two vaults of the same kind — two tenants, say — are two manifest entries with different names
/// built by the same factory.
/// </remarks>
public interface ICloudKeyVaultFactory
{
    /// <summary>Gets the kind this factory builds, matched against the manifest's <c>kind</c>.</summary>
    string Kind { get; }

    /// <summary>
    /// Describes the authorization server this vault signs in to, when it authenticates with OAuth2;
    /// null when it authenticates some other way. Required when the manifest sets a
    /// <c>credential</c> for the vault.
    /// </summary>
    /// <param name="settings">The vault, as the manifest and the developer's defaults together describe it.</param>
    OAuthOptions? Authorization(VaultOptions settings);

    /// <summary>Builds the vault one entry describes.</summary>
    /// <param name="settings">
    /// The vault, as the manifest and the developer's defaults together describe it. Two of its
    /// members are the library's rather than the package's: <see cref="VaultOptions.Kind"/> chose
    /// this factory, and <see cref="VaultOptions.Credential"/> was resolved into
    /// <paramref name="tokens"/>. A package reads its own <c>Settings</c> and <c>Secrets</c>.
    /// </param>
    /// <param name="tokens">
    /// The source of the bearer token this vault signs with, bound to the manifest's
    /// <c>credential</c> entry; null when the manifest set none.
    /// </param>
    Result<ICloudKeyVault> Create(VaultOptions settings, IAccessTokenSource? tokens);
}
