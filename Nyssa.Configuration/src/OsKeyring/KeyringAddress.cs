using NyssaAI.Results;

namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// Validates a keyring service and key and composes the target name both platform stores file a
/// secret under, so the validation contract cannot drift between them.
/// </summary>
internal static class KeyringAddress
{
    /// <summary>The longest composed target the stores are asked for.</summary>
    public const int MaxLength = 1024;

    /// <summary>Composes the <c>service/key</c> target, failing when either half is unusable.</summary>
    /// <param name="service">The service name the secret is filed under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    public static Result<string> Compose(string service, string key)
    {
        if (string.IsNullOrWhiteSpace(service))
            return Result<string>.Failure(Invalid("the service name is blank"));

        if (string.IsNullOrWhiteSpace(key))
            return Result<string>.Failure(Invalid("the key is blank"));

        if (service.Any(char.IsControl) || key.Any(char.IsControl))
            return Result<string>.Failure(Invalid("the address contains a control character"));

        var target = $"{service}/{key}";
        return target.Length > MaxLength
            ? Result<string>.Failure(Invalid($"the address is longer than {MaxLength} characters"))
            : Result<string>.Success(target);
    }

    /// <summary>The failure returned for a service or key the stores cannot be asked about.</summary>
    /// <param name="detail">Why the address was refused.</param>
    public static Error Invalid(string detail) => Error.FromMessages(
        "The credential address is not valid.",
        $"keyring: {detail}",
        ErrorCategory.Validation);
}
