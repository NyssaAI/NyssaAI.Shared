using System.Text.Json;
using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Manifest;

/// <summary>The source-generated context the manifest is read and written through, so both are AOT-safe.</summary>
[JsonSourceGenerationOptions(
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigurationManifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext
{
}
