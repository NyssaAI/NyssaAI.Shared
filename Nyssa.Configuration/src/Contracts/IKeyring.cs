using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IKeyring is the platform credential store this feature reads, writes, and deletes through: the Windows
/// Credential Manager, the macOS keychain, or a Secret Service implementation on Linux. It is the
/// custody boundary for material that must never be written to the configuration directory in the clear.
/// </summary>
/// <remarks>
/// Three outcomes are distinguishable: a secret, a success carrying null when nothing is filed
/// under the address, and a failure when the store could not answer at all. The distinction is
/// what lets the encryption key be created only when it is truly
/// absent: a store outage must never be mistaken for an absent key, or a fresh key would orphan
/// every value sealed under the old one.
///
/// An application registers its own implementation to replace the platform store — a test does
/// this to keep every run away from the real one.
/// </remarks>
public interface IKeyring
{
    /// <summary>Reads one secret, yielding a success carrying null when the store holds none under that address.</summary>
    /// <param name="service">The service name the secret is filed under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    /// <returns>The secret, null when absent, or a failure when the store could not be consulted.</returns>
    Result<string?> Get(string service, string key);

    /// <summary>Files one secret, replacing whatever was filed under that address before.</summary>
    /// <param name="service">The service name to file the secret under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    /// <param name="secret">The secret to store.</param>
    /// <returns>Success once the store has accepted it, or a failure when it could not.</returns>
    /// <remarks>
    /// A write has only two outcomes, unlike <see cref="Get"/>: there is no absence to report. A
    /// store that cannot be reached is a failure and never a silent no-op, because a caller that
    /// believes it has persisted a key would otherwise generate a different one next time and
    /// orphan everything sealed under the first.
    /// </remarks>
    Result Set(string service, string key, string secret);

    /// <summary>Deletes the secret at this address; an already absent secret is a success.</summary>
    /// <param name="service">The service name the secret is filed under.</param>
    /// <param name="key">The key of the secret within that service.</param>
    /// <returns>Success when deleted or absent; a failure for an invalid address or a store that refuses deletion.</returns>
    /// <remarks>
    /// Deleting an encryption key does not delete or re-encrypt configuration values protected by it.
    /// Keep the key while any such values are needed. A store may prompt for access; call off the UI thread.
    /// </remarks>
    Result Delete(string service, string key);
}
