using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IConfigurationProvider is the extension point for externalized secret storage: it turns a value
/// into a stored <see cref="ConfigurationValue"/>, resolves that entry back into a value, and
/// deletes what it stored when the value is removed or moves elsewhere.
/// </summary>
/// <remarks>
/// A provider that seals the value into the entry itself has no remote copy: removing or overwriting
/// the entry completes deletion. A provider holding the value outside the entry requests deletion
/// and reports whether its storage policy retains a copy.
/// </remarks>
internal interface IConfigurationProvider
{
    /// <summary>Gets the name entries use to select this provider.</summary>
    string Name { get; }

    /// <summary>True only when writes have no effects outside the returned local entry.</summary>
    bool IsLocalOnly => false;

    /// <summary>A non-secret diagnostic reference, when this provider can expose one.</summary>
    string? TargetReference(ConfigurationValue entry) => null;

    /// <summary>The optional vault capability used from the original attempt onward.</summary>
    CloudKeyVaults.ICloudKeyVaultRecovery? Recovery => null;

    /// <summary>Resolves a stored entry back into its value.</summary>
    /// <param name="entry">The stored entry to resolve.</param>
    /// <param name="namespace">The namespace the entry is addressed under.</param>
    /// <param name="key">The key the entry is addressed under.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value and returns the entry that will resolve back to it.</summary>
    /// <param name="value">The value to store.</param>
    /// <param name="namespace">The namespace to address the entry under.</param>
    /// <param name="key">The key to address the entry under.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default);

    /// <summary>Requests deletion of the stored value and reports any remaining remote copy.</summary>
    /// <param name="entry">The stored entry naming what to withdraw.</param>
    /// <param name="namespace">The namespace the entry is addressed under.</param>
    /// <param name="key">The key the entry is addressed under.</param>
    /// <param name="cancellationToken">Token used to cancel the withdrawal.</param>
    /// <returns>
    /// Success with RemoteValueRemains set when the provider retains a copy, including after soft
    /// deletion or when deletion is unsupported. Operational failures remain failures. Deleting an
    /// already absent value succeeds, so retries are safe.
    /// </returns>
    ValueTask<Result<ConfigurationRemoval>> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default);
}
