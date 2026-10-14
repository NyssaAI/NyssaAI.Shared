using NyssaAI.Configuration.Recovery;
using NyssaAI.Configuration.Values;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IConfigurationValues is the value surface a composed configuration root reads and writes through:
/// string values, their typed JSON forms, removal, and listing. Every member reports failure
/// through <see cref="Result"/> rather than by throwing.
/// </summary>
internal interface IConfigurationValues
{
    /// <summary>Reads one value, resolving it through its provider when it is not a literal.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry within the namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The resolved value, or a failure when the entry is missing, mismatched, or unreadable.</returns>
    ValueTask<Result<string>> GetAsync(string @namespace, string key, CancellationToken cancellationToken = default);

    /// <summary>Writes one string value, preserving an existing entry's provider.</summary>
    /// <param name="namespace">The namespace to hold the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="provider">
    /// Null uses default storage for a new entry or preserves an existing entry's provider.
    /// A named provider selects where a new value is stored. Naming a different provider for an
    /// existing entry is refused; use <see cref="MoveAsync"/> to change its destination.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <example>
    /// Default writes need no provider. Select a named provider when creating a value that needs it;
    /// subsequent writes preserve that choice.
    /// <code>
    /// await configuration.SetAsync("db", "host", "localhost");
    /// await configuration.SetAsync("db", "password", "s3cret-v1", "aesLocal");
    /// await configuration.SetAsync("db", "password", "s3cret-v2");
    /// </code>
    /// </example>
    ValueTask<Result> SetAsync(string @namespace, string key, string value, string? provider = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes through the provider, then removes the local entry and reports remote retention.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry within the namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the removal.</param>
    ValueTask<Result<ConfigurationRemoval>> RemoveAsync(string @namespace, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the interactive authorization behind one provider-backed entry and stores the
    /// credential it produces, clearing the entry's re-authorization flag.
    /// </summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry within the namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the flow.</param>
    /// <returns>
    /// Success once the replacement entry is stored, or a failure when the entry is missing, is a
    /// literal, names a provider that was not supplied, names one that does not implement
    /// <see cref="IReauthorizableProvider"/>, or when the flow itself did not complete.
    /// </returns>
    ValueTask<Result> ReauthorizeAsync(string @namespace, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the interactive authorization flow against the given server and stores the credential it
    /// produces at one address, under the provider that can refresh it.
    /// </summary>
    ValueTask<Result> AuthorizeAsync(string @namespace, string key, Client.OAuthOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires the credential a named provider authenticates with, at the address and against the
    /// server that provider declared.
    /// </summary>
    ValueTask<Result> AuthorizeProviderAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>Lists the keys held in one namespace, in ordinal order.</summary>
    /// <param name="namespace">The namespace to enumerate.</param>
    /// <param name="cancellationToken">Token used to cancel the enumeration.</param>
    ValueTask<Result<IReadOnlyList<string>>> ListAsync(string @namespace, CancellationToken cancellationToken = default);

    /// <summary>Reads one entry, or assembles the namespace when key is null, using reflection-based JSON.</summary>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask<Result<TValue>> GetAsAsync<TValue>(
        string @namespace,
        string? key,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one entry, or assembles the namespace when key is null, using source-generated JSON.</summary>
    ValueTask<Result<TValue>> GetAsAsync<TValue>(
        string @namespace,
        string? key,
        JsonTypeInfo<TValue> typeInfo,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces one entry, or the entire namespace when key is null, using reflection-based JSON.</summary>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask<ConfigurationAttempt> SetAsAsync<TValue>(
        string @namespace,
        string? key,
        TValue value,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces one entry, or the entire namespace when key is null, using source-generated JSON.</summary>
    ValueTask<ConfigurationAttempt> SetAsAsync<TValue>(
        string @namespace,
        string? key,
        TValue value,
        JsonTypeInfo<TValue> typeInfo,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Merges serialized object properties into a namespace, preserving omitted entries.</summary>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask<ConfigurationAttempt> MergeSettingsAsync<TValue>(string @namespace, TValue value, CancellationToken cancellationToken = default);

    /// <summary>Merges object properties using source-generated JSON, preserving omitted entries.</summary>
    ValueTask<ConfigurationAttempt> MergeSettingsAsync<TValue>(string @namespace, JsonTypeInfo<TValue> typeInfo, TValue value, CancellationToken cancellationToken = default);

    /// <summary>Moves one value to a different provider, withdrawing the copy it leaves behind.</summary>
    /// <param name="namespace">The namespace holding the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="toProvider">
    /// The destination provider, or null to return to default storage. Moving to the destination
    /// the value already uses is refused rather than treated as a no-op.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the move.</param>
    /// <returns>
    /// What the move did, including whether the source copy was withdrawn. A false
    /// <see cref="ConfigurationMove.SourceWithdrawn"/> means the value is safely readable at its new
    /// home but a copy survives at the old one, including after soft deletion; the same fact is logged as a warning.
    /// </returns>
    ValueTask<ConfigurationAttempt<ConfigurationMove>> MoveAsync(string @namespace, string key, string? toProvider = null, CancellationToken cancellationToken = default);
    /// <summary>Explicitly resumes a protected operation.</summary>
    ValueTask<ConfigurationAttempt<ConfigurationRecoveryOutcome>> RecoverAsync(ConfigurationRecoveryHandle handle, CancellationToken cancellationToken = default);
}
