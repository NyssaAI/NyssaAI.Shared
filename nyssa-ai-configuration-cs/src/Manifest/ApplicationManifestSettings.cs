namespace NyssaAI.Configuration.Manifest;

/// <summary>One application's section, settled: what the composer opens the store from.</summary>
internal sealed record ApplicationManifestSettings
{
    /// <summary>Gets the absolute configuration directory.</summary>
    public required string ConfigurationDirectory { get; init; }

    /// <summary>Gets the document lock timeout the section names, or null for the default.</summary>
    public TimeSpan? LockTimeout { get; init; }

    /// <summary>Gets the normalized root key vaults from the manifest, keyed by vault provider name.</summary>
    public IReadOnlyDictionary<string, VaultManifestEntry> KeyVaults { get; init; } =
        new Dictionary<string, VaultManifestEntry>(StringComparer.Ordinal);
}
