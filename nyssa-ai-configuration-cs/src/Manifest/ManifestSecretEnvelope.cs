using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// One AES-256-GCM encrypted secret entry inside <c>keyVaults.&lt;vaultName&gt;.secrets</c>.
/// </summary>
internal sealed record ManifestSecretEnvelope
{
    /// <summary>Gets the Base64URL-encoded FormatVersion 2 AES-256-GCM ciphertext.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Gets the local cipher provider name (<c>aesLocal</c> when specified).</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>Gets whether the entry is sealed (always <c>true</c> on disk).</summary>
    [JsonPropertyName("encrypted")]
    public required bool Encrypted { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        "ManifestSecretEnvelope { Value = [REDACTED], Encrypted = " + Encrypted + " }";
}
