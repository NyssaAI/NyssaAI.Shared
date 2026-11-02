namespace NyssaAI.Configuration.Manifest;

/// <summary>One application's section, settled: what the composer opens the store from.</summary>
internal sealed record ApplicationManifestSettings
{
    /// <summary>Gets the absolute configuration directory.</summary>
    public required string ConfigurationDirectory { get; init; }

    /// <summary>Gets the document lock timeout the section names, or null for the default.</summary>
    public TimeSpan? LockTimeout { get; init; }

    /// <summary>Gets the key vault the section names, normalized, or null when it names none.</summary>
    public VaultManifestEntry? KeyVault { get; init; }
}
