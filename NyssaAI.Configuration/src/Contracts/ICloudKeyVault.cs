using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// ICloudKeyVault is a remote secret store: it holds the value itself, and a configuration entry
/// holds only a <see cref="ConfigurationPointer"/> addressing it. It is the seam a concrete vault — a
/// Bitwarden project, an Azure Key Vault, a HashiCorp KV mount — is reached through.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not <see cref="IConfigurationProvider"/>. That contract resolves an entry
/// and knows about namespaces, sensitivity, and provider names; this one only moves a secret to and
/// from a store. A single adapter turns any implementation of this into a provider, the same way
/// <see cref="IConfigurationCipher"/> becomes one through the local-encryption provider, so a new vault is
/// written once against this contract rather than against the entry model.
/// </para>
/// <para>
/// Reads distinguish three outcomes, as every store in this package does: a secret, a success
/// carrying null when nothing is filed at that address, and a failure when the store could not
/// answer at all. Collapsing the last two would let an outage read as a deleted secret.
/// </para>
/// <para>
/// Explicit configuration removal and moves request deletion through this contract. Namespace
/// replacement only prunes local entries. The vault reports retained remote copies, including
/// recoverable soft-deleted secrets, instead of claiming that deletion always destroys the value.
/// </para>
/// </remarks>
public interface ICloudKeyVault
{
    /// <summary>Reads the secret filed at one address.</summary>
    /// <param name="pointer">The address the stored entry references.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The secret, null when the store holds nothing there, or a failure when it could not be consulted.</returns>
    ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default);

    /// <summary>Stores a secret and returns the address that will resolve back to it.</summary>
    /// <param name="namespace">The namespace the value is addressed under.</param>
    /// <param name="key">The key the value is addressed under.</param>
    /// <param name="value">The secret to store.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <returns>
    /// The pointer to record in the entry. It need not be the namespace and key given: a store is
    /// free to file the secret under an identifier of its own and return that.
    /// </returns>
    ValueTask<Result<ConfigurationPointer>> WriteAsync(
        string @namespace,
        string key,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>Requests deletion of the secret filed at one address and reports remote retention.</summary>
    /// <param name="pointer">The address the stored entry references.</param>
    /// <param name="cancellationToken">Token used to cancel the withdrawal.</param>
    /// <returns>
    /// Success with <see cref="ConfigurationRemoval.RemoteValueRemains"/> set to true if a copy is
    /// retained, including recoverable soft-deleted secrets or stores that do not support deletion.
    /// False when no copy remains. An already absent secret succeeds so removal can be retried;
    /// a soft-deleted copy still requires true on retry. Permission, connectivity, and other
    /// operational failures return failure rather than a retention flag.
    /// </returns>
    ValueTask<Result<ConfigurationRemoval>> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default);
}
