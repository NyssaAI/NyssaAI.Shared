using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Manifest;

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
