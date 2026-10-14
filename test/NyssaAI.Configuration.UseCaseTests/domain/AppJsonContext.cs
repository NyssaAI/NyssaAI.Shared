using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>
/// The serialization shape SomeApp-cli declares once for Native AOT. The library is handed
/// <c>AppJsonContext.Default.MailServerSettings</c> and never reflects over the type.
/// </summary>
[JsonSerializable(typeof(MailServerSettings))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSerializable(typeof(NamespaceMailSettings))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
