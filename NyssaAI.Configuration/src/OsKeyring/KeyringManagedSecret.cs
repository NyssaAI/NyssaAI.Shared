using System.Security.Cryptography;
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;


namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringManagedSecret gets, or on first use creates, random secret material held by the platform
/// credential store. It exists so a key never has to be written to the configuration directory:
/// the material is generated in memory, handed straight to the store, and read back from there on
/// every later run.
/// </summary>
/// <remarks>
/// <para>
/// The material is returned as bytes rather than as a cipher, so this stays inside the OsKeyring
/// boundary: OsKeyring owns credential custody and Encryption owns what a key is used for. A caller
/// composes the two, typically by passing the result to
/// <c>ConfigurationCipherAesGcm.Create</c>.
/// </para>
/// <para>
/// A read that fails is never treated as an absent secret. A store that is momentarily unreachable
/// would otherwise cause a fresh key to be generated and filed, orphaning every value sealed under
/// the previous one — a silent, unrecoverable data loss. Absence, and only absence, creates.
/// </para>
/// </remarks>
internal static class KeyringManagedSecret
{
    /// <summary>The shortest material worth holding, in bytes.</summary>
    public const int MinimumLength = 16;

    /// <summary>The longest material this helper will create, in bytes.</summary>
    public const int MaximumLength = 64;

    /// <summary>Reads the secret filed at one address, creating and filing it when absent.</summary>
    /// <param name="service">The service name the secret is filed under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    /// <param name="lengthInBytes">How much material to create when none is filed yet.</param>
    /// <param name="keyring">The store to use; the platform default when null.</param>
    /// <returns>
    /// The material and whether this call created it, or a failure when the store could not be
    /// consulted, refused the write, or holds something that is not material of the requested length.
    /// </returns>
    public static Result<(byte[] Material, bool Created)> GetOrCreate(string service, string key, int lengthInBytes, IKeyring? keyring = null)
    {
        var existing = Get(service, key, lengthInBytes, keyring ??= KeyringDefault.Create());
        if (existing.IsFailure)
            return Result<(byte[], bool)>.Failure(existing.Errors);

        if (existing.Value is not null)
            return Result<(byte[], bool)>.Success((existing.Value, false));

        var created = RandomNumberGenerator.GetBytes(lengthInBytes);
        var encoded = Convert.ToBase64String(created);
        var stored = keyring.Set(service, key, encoded);
        return stored.IsFailure
            ? Result<(byte[], bool)>.Failure(stored.Errors)
            : Result<(byte[], bool)>.Success((created, true));
    }

    /// <summary>Reads the secret filed at one address, yielding null when none is filed. Never creates.</summary>
    /// <param name="service">The service name the secret is filed under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    /// <param name="lengthInBytes">The length the material must decode to.</param>
    /// <param name="keyring">The store to use; the platform default when null.</param>
    /// <remarks>
    /// This is the administered-machine path: the manifest says an administrator placed the key, so
    /// absence is theirs to fix and this process must not invent material that would orphan every
    /// value they seal later.
    /// </remarks>
    public static Result<byte[]?> Get(string service, string key, int lengthInBytes, IKeyring? keyring = null)
    {
        if (lengthInBytes is < MinimumLength or > MaximumLength)
            return Result<byte[]?>.Failure(LengthInvalid(lengthInBytes));

        keyring ??= KeyringDefault.Create();

        var existing = keyring.Get(service, key);
        if (existing.IsFailure)
            return Result<byte[]?>.Failure(existing.Errors);

        if (existing.Value is null)
            return Result<byte[]?>.Success(null);

        return TryDecode(existing.Value, lengthInBytes, out var material)
            ? Result<byte[]?>.Success(material)
            : Result<byte[]?>.Failure(SecretMalformed(service, key, lengthInBytes));
    }

    /// <summary>The failure returned for a length outside the supported range.</summary>
    /// <param name="lengthInBytes">The length that was asked for.</param>
    public static Error LengthInvalid(int lengthInBytes) => Error.FromMessages(
        "The secret could not be created.",
        $"keyring: {lengthInBytes} bytes is outside the supported range of {MinimumLength} to {MaximumLength}",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the store holds something that is not usable material.</summary>
    /// <param name="service">The service name the secret is filed under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    /// <param name="lengthInBytes">The length that was expected.</param>
    /// <remarks>The stored value itself is never reported, only its address and expected shape.</remarks>
    public static Error SecretMalformed(string service, string key, int lengthInBytes) =>
        MaterialMalformed($"{service}/{key}", lengthInBytes);

    /// <summary>The failure returned when material at some address is not usable.</summary>
    /// <param name="source">How the material is addressed.</param>
    /// <param name="lengthInBytes">The length that was expected.</param>
    /// <remarks>The material itself is never reported, only its address and expected shape.</remarks>
    public static Error MaterialMalformed(string source, int lengthInBytes) => Error.FromMessages(
        "The stored secret is not usable.",
        $"keyring: '{source}' does not hold {lengthInBytes} bytes of base64 material",
        ErrorCategory.Validation);

    private static bool TryDecode(string encoded, int lengthInBytes, out byte[] material)
    {
        material = new byte[lengthInBytes];
        return Convert.TryFromBase64String(encoded, material, out var written) && written == lengthInBytes;
    }
}
