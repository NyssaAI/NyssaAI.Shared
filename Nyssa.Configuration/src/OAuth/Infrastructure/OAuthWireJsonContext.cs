using System.Text.Json.Serialization;



namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// The source-generated JSON context for the token endpoint's wire format, which is snake_case
/// and therefore separate from <see cref="OAuthGrantJsonContext"/>, which is the stored form.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(OAuthTokenResponse))]
internal sealed partial class OAuthWireJsonContext : JsonSerializerContext
{
}
