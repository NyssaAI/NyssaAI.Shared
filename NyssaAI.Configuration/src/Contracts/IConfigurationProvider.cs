using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IConfigurationProvider is the extension point for externalized secret storage: it turns a value
/// into a stored <see cref="ConfigurationValue"/>, resolves that entry back into a value, and
/// withdraws what it stored when the value moves elsewhere.
/// </summary>
/// <remarks>
/// A provider that seals the value into the entry itself has nothing to withdraw: overwriting the
/// entry is the withdrawal, and <see cref="EraseAsync"/> succeeds without doing anything. A provider
/// holding the value outside the entry must actually withdraw it, because otherwise moving a secret
/// away leaves a copy nobody is tracking.
/// </remarks>
internal interface IConfigurationProvider
{
    /// <summary>Gets the name entries use to select this provider.</summary>
    string Name { get; }

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

    /// <summary>Withdraws the stored value, so this provider no longer serves it.</summary>
    /// <param name="entry">The stored entry naming what to withdraw.</param>
    /// <param name="namespace">The namespace the entry is addressed under.</param>
    /// <param name="key">The key the entry is addressed under.</param>
    /// <param name="cancellationToken">Token used to cancel the withdrawal.</param>
    /// <returns>
    /// Success once this provider no longer serves the value. A backing store that only soft-deletes
    /// or disables still counts: the requirement is that a read through this provider stops
    /// resolving, not that every recoverable trace is destroyed.
    /// </returns>
    ValueTask<Result> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default);
}
