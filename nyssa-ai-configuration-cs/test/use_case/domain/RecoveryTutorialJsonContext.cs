using System.Text.Json;
using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.UseCaseTests.domain;

[JsonSerializable(typeof(JsonElement))]
internal sealed partial class RecoveryTutorialJsonContext : JsonSerializerContext;
