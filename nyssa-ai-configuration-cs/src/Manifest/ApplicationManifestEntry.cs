using System.Text.Json;
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

    /// <summary>
    /// Gets any legacy per-application <c>keyVault</c> element solely so the reader can enforce
    /// the private-file gate first and reject legacy per-application vault sections with a clear
    /// migration error. Always omitted when writing.
    /// </summary>
    [JsonPropertyName("keyVault")]
    public JsonElement? KeyVault { get; init; }

    /// <summary>Returns a redacted representation that omits any legacy <c>keyVault</c> JSON element.</summary>
    public override string ToString() =>
        $"ApplicationManifestEntry {{ ConfigurationDirectory = {ConfigurationDirectory}, LockTimeout = {LockTimeout}, LockTimeoutMs = {LockTimeoutMs} }}";
}
