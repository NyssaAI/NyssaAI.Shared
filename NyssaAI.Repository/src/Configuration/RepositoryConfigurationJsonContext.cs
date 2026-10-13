using System.Text.Json.Serialization;

namespace NyssaAI.Repository.Configuration;

/// <summary>RepositoryConfigurationJsonContext supplies generated metadata for the repository settings contract.</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RepositoryConfiguration))]
internal sealed partial class RepositoryConfigurationJsonContext : JsonSerializerContext
{
}
