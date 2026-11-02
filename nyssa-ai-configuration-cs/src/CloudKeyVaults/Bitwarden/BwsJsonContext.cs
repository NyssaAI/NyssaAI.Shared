using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.CloudKeyVaults.Bitwarden;

/// <summary>
/// Source-generated JSON serialization context for Bitwarden Secrets Manager CLI (<c>bws</c>) payloads.
/// </summary>
[JsonSerializable(typeof(BwsSecretEntry))]
[JsonSerializable(typeof(List<BwsSecretEntry>))]
internal sealed partial class BwsJsonContext : JsonSerializerContext
{
}
