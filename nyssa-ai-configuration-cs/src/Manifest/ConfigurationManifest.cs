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
