using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// ConfigurationManifest is the on-disk shape of <c>~/.nyssa-ai/config-manifest.json</c>: one file
/// per user, one section per application name. A section says where that application's
/// configuration directory is, which key vault it uses, and its lock timeout. Encryption is never
/// mentioned because it is never optional.
/// </summary>
/// <remarks>
/// The same schema is read from the default path and from a path the developer codes, and it is
/// what the application's <c>init</c> command writes a section into. <c>version</c> is the schema
/// version and only <c>1</c> is accepted; a file declaring anything else is refused rather than
/// guessed at.
/// </remarks>
internal sealed record ConfigurationManifest
{
    /// <summary>The one schema version this reader accepts.</summary>
    public const int SupportedVersion = 1;

    /// <summary>Gets the schema version.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = SupportedVersion;

    /// <summary>Gets the sections, keyed by application name.</summary>
    [JsonPropertyName("applications")]
    public Dictionary<string, ApplicationManifestEntry>? Applications { get; init; }
}

/// <summary>One application's section: its bootstrap dependencies.</summary>
internal sealed record ApplicationManifestEntry
{
    /// <summary>Gets the configuration directory, absolute or relative to the manifest's folder.</summary>
    [JsonPropertyName("configurationDirectory")]
    public string? ConfigurationDirectory { get; init; }

    /// <summary>Gets the bounded wait for a document lock, as a TimeSpan string, or null for the default.</summary>
    [JsonPropertyName("lockTimeout")]
    public string? LockTimeout { get; init; }

    /// <summary>Gets the bounded wait for a document lock in integer milliseconds, or null for the default.</summary>
    [JsonPropertyName("lockTimeoutMs")]
    public int? LockTimeoutMs { get; init; }

    /// <summary>Gets the key vault this application reads sealed values through, or null when it uses none.</summary>
    [JsonPropertyName("keyVault")]
    public VaultManifestEntry? KeyVault { get; init; }
}

/// <summary>The key vault in a section.</summary>
internal sealed record VaultManifestEntry
{
    /// <summary>Gets the provider name stored entries record.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets the kind, matched against a registered <c>ICloudKeyVaultFactory.Kind</c>.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>Gets the vault's endpoint, if the package needs one.</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>
    /// Gets the <c>namespace/key</c> of the entry this vault authenticates with, or null when the
    /// vault authenticates some other way. The entry is an ordinary sealed <c>oauth</c> entry in
    /// local storage, and may never itself be stored in this vault.
    /// </summary>
    [JsonPropertyName("credential")]
    public string? Credential { get; init; }

    /// <summary>Gets the package's own settings, as string pairs the library does not interpret.</summary>
    [JsonPropertyName("settings")]
    public Dictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// Gets the package's own secrets — a client secret, an API key — as string pairs the library
    /// does not interpret, never logs, and never repeats in an error. Their presence makes the
    /// manifest's file permissions a startup check.
    /// </summary>
    [JsonPropertyName("secrets")]
    public Dictionary<string, string>? Secrets { get; init; }
}
