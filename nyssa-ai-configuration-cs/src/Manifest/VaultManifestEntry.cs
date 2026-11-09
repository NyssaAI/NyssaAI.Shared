using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Manifest;

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

    /// <summary>Gets the secret cache time-to-live in seconds (defaults to 60; 0 disables caching).</summary>
    [JsonPropertyName("cacheTTLSeconds")]
    public int? CacheTTLSeconds { get; init; }

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
