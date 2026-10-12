using System.Text.Json.Serialization;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// ConfigurationValue is one stored configuration value: either a literal, or a reference resolved by the
/// named provider. Instances are immutable; <see cref="Validate"/> reports whether the combination
/// of flags is coherent.
/// </summary>
internal sealed record ConfigurationValue
{
    /// <summary>Gets the stored value: the literal itself, or the provider-specific reference.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Value"/> is sealed by a cipher.</summary>
    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; init; }

    /// <summary>Gets the name of the provider that resolves this entry, empty for a literal.</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the provider needs the user to re-authorize.</summary>
    [JsonPropertyName("reauth_required")]
    public bool ReauthRequired { get; init; }

    /// <summary>Gets a value indicating whether this entry carries its value directly.</summary>
    [JsonIgnore]
    public bool IsLiteral => string.IsNullOrEmpty(Provider);

    /// <summary>
    /// Checks the entry's own invariant: an encrypted entry must name the provider that can open
    /// it. Returns a validation failure rather than throwing, per the project's failure contract.
    /// </summary>
    public Result Validate() =>
        Encrypted && IsLiteral
            ? Result.Failure(EncryptedEntryNeedsProvider)
            : Result.Success();

    /// <summary>The failure returned when an entry claims encryption but names no provider.</summary>
    public static Error EncryptedEntryNeedsProvider { get; } = Error.FromMessages(
        "The configuration entry is not readable.",
        "An encrypted entry must name a provider.",
        ErrorCategory.Validation);
}
