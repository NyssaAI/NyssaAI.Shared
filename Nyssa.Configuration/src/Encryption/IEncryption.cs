using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Encryption;

/// <summary>
/// IEncryption is the Encryption feature's covering contract: how the client area obtains a cipher
/// over key material, and the provider that seals secrets into the configuration document with it,
/// without naming either concrete type.
/// </summary>
/// <remarks>
/// The feature owns sealing and nothing else. Where the key comes from is
/// <see cref="OsKeyring"/>'s job, and which entries are secret is the store's, so neither appears
/// here.
/// </remarks>
internal interface IEncryption
{
    /// <summary>Creates the address-binding cipher over 32 bytes of key material.</summary>
    /// <param name="key">The AES-256 key. The caller keeps ownership of the array.</param>
    /// <returns>A failure when the key is not exactly 32 bytes.</returns>
    Result<IConfigurationCipher> OpenCipher(byte[] key);

    /// <summary>Creates the provider that stores secrets sealed in the document itself.</summary>
    /// <param name="cipher">The cipher the provider seals and opens with.</param>
    /// <param name="name">The name entries use to select the provider.</param>
    IConfigurationProvider OpenProvider(IConfigurationCipher cipher, string name = "aesLocal");
}
