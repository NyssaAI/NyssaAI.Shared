using System.Text.Json.Serialization;

namespace NyssaAI.DbGateway.Configuration;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DbGatewayConfiguration))]
internal sealed partial class DbGatewayConfigurationJsonContext : JsonSerializerContext;
