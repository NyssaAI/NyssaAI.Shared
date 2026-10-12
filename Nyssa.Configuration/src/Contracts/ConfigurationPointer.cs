using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// ConfigurationPointer is a provider-qualified reference to one configuration value, addressed by
/// namespace and key. Construct one through <see cref="Parse"/>, which validates the address.
/// </summary>
public sealed record ConfigurationPointer
{
    /// <summary>Gets the name of the provider that resolves this pointer.</summary>
    public required string Provider { get; init; }

    /// <summary>Gets the namespace half of the address.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the key half of the address.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the wire form of the address, <c>namespace/key</c>.</summary>
    public string Value => $"{Namespace}/{Key}";

    /// <summary>
    /// Parses a <c>namespace/key</c> address for the named provider. Returns a validation failure
    /// when the provider is blank or the address is not exactly two non-empty components.
    /// </summary>
    /// <param name="provider">The name of the provider that resolves the pointer.</param>
    /// <param name="value">The <c>namespace/key</c> address to parse.</param>
    public static Result<ConfigurationPointer> Parse(string provider, string value)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrEmpty(value))
            return Result<ConfigurationPointer>.Failure(InvalidPointer);

        var parts = value.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty))
            return Result<ConfigurationPointer>.Failure(InvalidPointer);

        return Result<ConfigurationPointer>.Success(new ConfigurationPointer
        {
            Provider = provider,
            Namespace = parts[0],
            Key = parts[1],
        });
    }

    /// <summary>The failure returned when a pointer address cannot be parsed.</summary>
    public static Error InvalidPointer { get; } = Error.FromMessages(
        "The configuration reference is not valid.",
        "The configuration pointer is invalid.",
        ErrorCategory.Validation);
}
