using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Encryption;

/// <summary>The one implementation of <see cref="IEncryption"/>, sealing with AES-256-GCM.</summary>
internal sealed class LocalEncryption : IEncryption
{
    /// <inheritdoc/>
    public Result<IConfigurationCipher> OpenCipher(byte[] key)
    {
        var cipher = ConfigurationCipherAesGcm.Create(key);
        return cipher.IsFailure
            ? Result<IConfigurationCipher>.Failure(cipher.Errors)
            : Result<IConfigurationCipher>.Success(cipher.Value);
    }

    /// <inheritdoc/>
    public IConfigurationProvider OpenProvider(IConfigurationCipher cipher, string name = "aesLocal") =>
        new ConfigurationProviderLocalEncryption(cipher, name);
}
