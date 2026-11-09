using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// VaultOptions describes the key vault: the typed form of the <c>keyVault</c> entry in a
/// manifest section. <see cref="Name"/> is the provider name stored entries record, so changing it
/// orphans everything already sealed through the vault.
/// </summary>
public sealed class VaultOptions
{
    /// <summary>The default cache TTL in seconds when <see cref="CacheTTLSeconds"/> is not specified.</summary>
    public const int DefaultCacheTTLSeconds = 60;

    /// <summary>Gets or sets the provider name stored entries record.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the kind, matched against a registered <see cref="ICloudKeyVaultFactory.Kind"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>Gets or sets the vault's endpoint, if the package needs one. Must be absolute.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the <c>namespace/key</c> of the entry this vault authenticates with, or null
    /// when the vault authenticates some other way. The entry is an ordinary sealed <c>oauth</c>
    /// entry in local storage, and may never itself be stored in this vault.
    /// </summary>
    public string? Credential { get; set; }

    /// <summary>Gets or sets the secret cache TTL in seconds. Zero disables caching; defaults to 60.</summary>
    public int CacheTTLSeconds { get; set; } = DefaultCacheTTLSeconds;

    /// <summary>Gets the package's own settings, as string pairs the library does not interpret.</summary>
    public IDictionary<string, string> Settings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the package's own secrets, as string pairs the library does not interpret, log, or
    /// repeat. In code these are for tests; a deployed application keeps them in its manifest.
    /// </summary>
    public IDictionary<string, string> Secrets { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
