using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Encryption;

/// <summary>
/// ConfigurationCipherAesGcm seals configuration values with AES-256-GCM. The namespace and key are
/// authenticated as GCM associated data, so a sealed value moved to another address fails to
/// open. Instances are created through <see cref="Create"/>, which checks the key length.
/// </summary>
/// <remarks>
/// <para>
/// Sealed form, before base64url encoding: one version byte (<c>2</c>), a 12-byte nonce, the
/// 16-byte tag, then the ciphertext of the value alone. The address travels as associated data
/// rather than inside the plaintext, so a namespace or key may contain any character -- an
/// earlier format delimited the address with <c>|</c> inside the plaintext, and a key containing
/// that character sealed successfully and could never be opened again.
/// </para>
/// <para>
/// Because the address is authenticated rather than compared, a relocated value and a tampered
/// value are the same event to the cipher: authentication fails. Both are reported as
/// <see cref="SealedValueNotAuthentic"/>.
/// </para>
/// </remarks>
internal sealed class ConfigurationCipherAesGcm : IConfigurationCipher
{
    private const byte FormatVersion = 2;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;
    private const int HeaderLength = 1 + NonceLength + TagLength;

    private readonly byte[] _key;

    private ConfigurationCipherAesGcm(byte[] key) => _key = key;

    /// <summary>Creates a cipher over a 32-byte AES-256 key, failing on any other length.</summary>
    /// <param name="key">The 32-byte key material; the cipher keeps its own copy.</param>
    public static Result<ConfigurationCipherAesGcm> Create(byte[] key)
    {
        if (key is null || key.Length != KeyLength)
            return Result<ConfigurationCipherAesGcm>.Failure(KeyLengthInvalid(key?.Length ?? 0));

        return Result<ConfigurationCipherAesGcm>.Success(new ConfigurationCipherAesGcm([.. key]));
    }

    /// <inheritdoc/>
    public Result<string> Seal(string value, string @namespace, string key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(_key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(@namespace, key));

        var payload = new byte[HeaderLength + ciphertext.Length];
        payload[0] = FormatVersion;
        nonce.CopyTo(payload, 1);
        tag.CopyTo(payload, 1 + NonceLength);
        ciphertext.CopyTo(payload, HeaderLength);

        return Result<string>.Success(
            Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }

    /// <inheritdoc/>
    public Result<string> Open(string value, string @namespace, string key)
    {
        var encoded = value.Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');

        // This is the adapter boundary: Convert and AesGcm throw on malformed or tampered input,
        // and every one of those outcomes is a routine failure the caller must be able to handle.
        try
        {
            var payload = Convert.FromBase64String(encoded);
            if (payload.Length < HeaderLength || payload[0] != FormatVersion)
                return Result<string>.Failure(SealedValueMalformed);

            var plaintext = new byte[payload.Length - HeaderLength];
            using var aes = new AesGcm(_key, TagLength);
            aes.Decrypt(
                payload.AsSpan(1, NonceLength),
                payload.AsSpan(HeaderLength),
                payload.AsSpan(1 + NonceLength, TagLength),
                plaintext,
                AssociatedData(@namespace, key));

            return Result<string>.Success(Encoding.UTF8.GetString(plaintext));
        }
        catch (FormatException)
        {
            return Result<string>.Failure(SealedValueMalformed);
        }
        catch (CryptographicException)
        {
            return Result<string>.Failure(SealedValueNotAuthentic);
        }
    }

    /// <summary>The failure returned when the key material is not 32 bytes.</summary>
    /// <param name="actualLength">The length that was supplied.</param>
    public static Error KeyLengthInvalid(int actualLength) => Error.FromMessages(
        "The encryption key is not usable.",
        $"AES-256 requires a 32-byte key; {actualLength} bytes were supplied.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a sealed value is truncated, or not this cipher's format.</summary>
    public static Error SealedValueMalformed { get; } = Error.FromMessages(
        "The stored secret could not be read.",
        "The sealed value is not a well-formed version 2 AES-256-GCM payload.",
        ErrorCategory.Validation);

    /// <summary>
    /// The failure returned when a sealed value does not authenticate: it was tampered with, or it
    /// was sealed against a different namespace and key.
    /// </summary>
    public static Error SealedValueNotAuthentic { get; } = Error.FromMessages(
        "The stored secret could not be read.",
        "The sealed value failed authentication: it was altered, or sealed against a different namespace and key.",
        ErrorCategory.Validation);

    // Length-prefixed rather than delimited, so no character in a namespace or key can make two
    // addresses encode the same way.
    internal static byte[] AssociatedData(string @namespace, string key)
    {
        var namespaceBytes = Encoding.UTF8.GetBytes(@namespace);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var data = new byte[1 + 4 + namespaceBytes.Length + 4 + keyBytes.Length];
        data[0] = FormatVersion;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1), namespaceBytes.Length);
        namespaceBytes.CopyTo(data, 5);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(5 + namespaceBytes.Length), keyBytes.Length);
        keyBytes.CopyTo(data, 9 + namespaceBytes.Length);
        return data;
    }
}
