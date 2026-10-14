using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Encryption;

/// <summary>
/// ConfigurationProviderLocalEncryption stores secrets in the configuration document itself,
/// sealed by an <see cref="IConfigurationCipher"/> rather than held by an external secret service.
/// </summary>
internal sealed class ConfigurationProviderLocalEncryption : IConfigurationProvider
{
    public bool IsLocalOnly => true;

    private readonly IConfigurationCipher _cipher;

    /// <summary>Creates a provider over one cipher.</summary>
    /// <param name="cipher">The cipher that seals and opens stored values.</param>
    /// <param name="name">The name entries use to select this provider.</param>
    public ConfigurationProviderLocalEncryption(IConfigurationCipher cipher, string name = "aesLocal")
    {
        _cipher = cipher;
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_cipher.Open(entry.Value, @namespace, key));

    /// <inheritdoc/>
    public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default)
    {
        var sealedValue = _cipher.Seal(value, @namespace, key);
        return ValueTask.FromResult(sealedValue.IsFailure
            ? Result<ConfigurationValue>.Failure(sealedValue.Errors)
            : Result<ConfigurationValue>.Success(new ConfigurationValue
            {
                Value = sealedValue.Value,
                Encrypted = true,
                Provider = Name,
            }));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The ciphertext is the entry's own value, so there is nothing outside it to withdraw:
    /// removing or overwriting the entry completes deletion.
    /// </remarks>
    public ValueTask<Result<ConfigurationRemoval>> EraseAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false }));
}
