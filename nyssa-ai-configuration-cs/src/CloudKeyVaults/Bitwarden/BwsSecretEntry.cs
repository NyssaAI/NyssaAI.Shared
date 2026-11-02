using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.CloudKeyVaults.Bitwarden;

/// <summary>
/// Represents a secret entry returned by the Bitwarden Secrets Manager CLI (<c>bws</c>).
/// </summary>
internal sealed record BwsSecretEntry
{
    /// <summary>Gets the Bitwarden secret UUID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Gets the secret key name returned by <c>bws</c>.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>Gets the legacy secret slug returned by older <c>bws</c> fixtures.</summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    /// <summary>Gets the secret value returned by <c>bws secret get</c>.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    /// <summary>Gets the resolved secret key name.</summary>
    [JsonIgnore]
    public string ResolvedKey => Key ?? Slug ?? string.Empty;
}
