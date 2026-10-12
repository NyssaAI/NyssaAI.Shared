using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>
/// AccessTokenSource is what a vault package receives: one call that yields the bearer token for
/// the credential the manifest bound to that vault. Behind it, the store reads the entry through
/// the <c>oauth</c> provider, so refresh, rotation, locking, and the re-authorization mark all
/// happen without the package knowing any of it exists.
/// </summary>
/// <remarks>
/// The store is reached through a reference filled in after composition, because the vault needs
/// the source before the store exists and the store needs the vault to exist. Until the reference
/// is filled, a call is a failure rather than a null dereference.
/// </remarks>
internal sealed class AccessTokenSource(string provider, StoreReference store) : IAccessTokenSource
{
    /// <inheritdoc/>
    public ValueTask<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        store.Current is { } current
            ? current.ResolveCredentialAsync(provider, cancellationToken)
            : ValueTask.FromResult(Result<string>.Failure(NotComposed(provider)));

    /// <summary>The failure returned when the token source is used before the store that serves it exists.</summary>
    public static Error NotComposed(string provider) => Error.FromMessages(
        "The configuration store is not ready.",
        $"configuration: provider '{provider}' asked for a token before the store was composed",
        ErrorCategory.Unexpected);
}

/// <summary>Holds the store once it exists, for token sources created before it did.</summary>
internal sealed class StoreReference
{
    public ConfigurationStore? Current { get; set; }
}
