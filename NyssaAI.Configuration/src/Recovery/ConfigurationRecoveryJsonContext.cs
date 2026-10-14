using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Recovery;

[JsonSerializable(typeof(ConfigurationRecoveryState))]
internal sealed partial class ConfigurationRecoveryJsonContext : JsonSerializerContext;
