using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>
/// CloudKeyVaultProvider adapts any <see cref="ICloudKeyVault"/> to the pluggable provider contract,
/// so a remote secret store is written once against the vault contract and never against the entry model.
/// </summary>
/// <remarks>
/// The stored entry holds a <see cref="ConfigurationPointer"/>, not a value, so nothing sensitive is
/// written to the configuration document and <c>Encrypted</c> stays false — there is no local
/// ciphertext to open, only an address to follow.
/// </remarks>
internal sealed class CloudKeyVaultProvider : IConfigurationProvider
{
    private readonly ICloudKeyVault _vault;

    /// <summary>Creates a provider over one vault.</summary>
    /// <param name="vault">The store that holds the secrets.</param>
    /// <param name="name">The name entries use to select this provider.</param>
    /// <remarks>
    /// Name it after the authority rather than the mechanism — <c>bitwarden</c> or
    /// <c>platform-vault</c> — because the name is what each entry records, and outlives the
    /// endpoint or tenant the vault happens to point at.
    /// </remarks>
    public CloudKeyVaultProvider(ICloudKeyVault vault, string name = "vault")
    {
        _vault = vault;
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    public string? TargetReference(ConfigurationValue entry) => entry.Value;

    public ICloudKeyVaultRecovery? Recovery => _vault as ICloudKeyVaultRecovery;

    /// <inheritdoc/>
    public async ValueTask<Result<string>> ReadAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        var pointer = ConfigurationPointer.Parse(Name, entry.Value);
        if (pointer.IsFailure)
            return Result<string>.Failure(pointer.Errors);

        var secret = await _vault.ReadAsync(pointer.Value, cancellationToken).ConfigureAwait(false);
        if (secret.IsFailure)
            return Result<string>.Failure(secret.Errors);

        // Absence is a failure here, unlike at the store itself: the entry asserts the secret
        // exists, so nothing filed at that address is a broken reference, not an empty value.
        return secret.Value is null
            ? Result<string>.Failure(SecretNotFound(@namespace, key, pointer.Value.Value))
            : Result<string>.Success(secret.Value);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationValue>> WriteAsync(
        string value,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        var pointer = await _vault.WriteAsync(@namespace, key, value, cancellationToken).ConfigureAwait(false);
        return pointer.IsFailure
            ? Result<ConfigurationValue>.Failure(pointer.Errors)
            : Result<ConfigurationValue>.Success(new ConfigurationValue
            {
                Value = pointer.Value.Value,
                Provider = Name,
            });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is the provider that actually has something to withdraw. The entry holds only an
    /// address, so overwriting it would leave the secret in the vault with nothing pointing at it;
    /// explicit removal and moves call this and surface any retention reported by the vault.
    /// </remarks>
    public async ValueTask<Result<ConfigurationRemoval>> EraseAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        var pointer = ConfigurationPointer.Parse(Name, entry.Value);
        if (pointer.IsFailure)
            return Result<ConfigurationRemoval>.Failure(pointer.Errors);

        return await _vault.RemoveAsync(pointer.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The failure returned when the vault holds nothing at the address an entry references.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry.</param>
    /// <param name="address">The address the entry referenced, which is not itself a secret.</param>
    public static Error SecretNotFound(string @namespace, string key, string address) => Error.FromMessages(
        "The configuration value is no longer available.",
        $"vault: '{@namespace}/{key}' references '{address}', which the store does not hold",
        ErrorCategory.Validation);
}
