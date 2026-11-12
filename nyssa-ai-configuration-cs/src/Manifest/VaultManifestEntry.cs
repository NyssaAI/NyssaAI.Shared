using System.Text.Json;
using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Manifest;

/// <summary>One root <c>keyVaults</c> entry in the configuration manifest.</summary>
internal sealed record VaultManifestEntry
{
    /// <summary>
    /// Gets the provider name stored entries record. Optional on disk because the root
    /// <c>keyVaults</c> map key is canonical; normalized in memory to the map key and omitted
    /// when writing.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Gets the kind, matched against a registered <c>ICloudKeyVaultFactory.Kind</c>.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

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
    /// Gets the raw wire <c>secrets</c> JSON element during deserialization and serialization so
    /// unencrypted or malformed secret values are caught by private-file and envelope validation
    /// rather than raw JSON type errors.
    /// </summary>
    [JsonPropertyName("secrets")]
    public JsonElement? RawSecrets { get; init; }

    /// <summary>
    /// Gets the normalized encrypted secret envelopes for this vault, keyed by secret name.
    /// Never logged and never repeated in an error; their presence makes the manifest's file
    /// permissions a startup check.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, ManifestSecretEnvelope>? Secrets { get; init; }

    /// <summary>Returns a redacted representation that never prints raw or sealed secret payloads.</summary>
    public override string ToString() =>
        $"VaultManifestEntry {{ Name = {Name}, Kind = {Kind}, Endpoint = {Endpoint}, Secrets = [{Secrets?.Count ?? 0} redacted] }}";
}
