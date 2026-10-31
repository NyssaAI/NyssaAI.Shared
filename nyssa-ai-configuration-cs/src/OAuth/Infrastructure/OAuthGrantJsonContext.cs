using NyssaAI.Configuration.Contracts;
using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// The source-generated JSON context for the grant this feature seals into a configuration value.
/// </summary>
/// <remarks>
/// Separate from <see cref="OAuthWireJsonContext"/>, which is the snake_case shape the token
/// endpoint returns. This one is the stored form, and it lives here rather than in the Values
/// feature's context because the shape is this feature's to change.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(OAuthToken))]
internal sealed partial class OAuthGrantJsonContext : JsonSerializerContext
{
}
