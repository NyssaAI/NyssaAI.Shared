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

    /// <summary>Writes one value, storing it through the provider that will serve it.</summary>
    /// <param name="namespace">The namespace to hold the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="provider">
    /// Where the value is stored. Null inherits the destination the existing entry already records,
    /// and fails on a key that has none yet rather than guessing. Pass <see cref="IConfiguration.Plain"/> to store
    /// the value unprotected. Naming a provider that disagrees with an existing entry is refused:
    /// that is a move, and <see cref="MoveAsync"/> owns it because it has a source copy to withdraw.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <example>
    /// A destination is stated when the key is created and inherited on every write after it. The
    /// second call's brevity is the point: it does not need to know where the value lives, and there
    /// is no argument it could have left out that would have stored the new value in plaintext.
    /// <code>
    /// await configuration.SetAsync("db", "password", "s3cret-v1", "aesLocal");
    /// await configuration.SetAsync("db", "password", "s3cret-v2");
    /// </code>
    /// Restating the destination is legal, because a write is refused only when it <em>disagrees</em>
    /// with the entry. Inheritance is a default rather than a mandate: code that would rather see the
    /// destination at every call site can pass it every time, and gets a hard failure the day that
    /// assumption drifts from what is on disk.
    /// <code>
    /// await configuration.SetAsync("db", "password", "s3cret-v2", "aesLocal");
    /// </code>
    /// Unprotected storage is never reached by omission -- it has to be named.
    /// <code>
    /// await configuration.SetAsync("db", "host", "localhost", IConfiguration.Plain);
    /// </code>
    /// The two ways a write can disagree with the stored entry both stop rather than guess.
    /// <code>
    /// await configuration.SetAsync("api", "clientSecret", secret);
    /// // fails: 'api/clientSecret' does not exist, so there is no entry to inherit a provider from
    ///
    /// await configuration.SetAsync("db", "password", "s3cret-v3", "vault");
    /// // fails: 'db/password' is held by 'aesLocal' -- that is a move, so use MoveAsync
    /// </code>
    /// </example>
    ValueTask<Result> SetAsync(string @namespace, string key, string value, string? provider = null, CancellationToken cancellationToken = default);

    /// <summary>Removes one entry, succeeding when the entry was already absent.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry within the namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the removal.</param>
    ValueTask<Result> RemoveAsync(string @namespace, string key, CancellationToken cancellationToken = default);

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

    /// <summary>Writes one entry, or the object properties when key is null, using reflection-based JSON.</summary>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask<Result> SetAsAsync<TValue>(
        string @namespace,
        string? key,
        TValue value,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one entry, or the object properties when key is null, using source-generated JSON.</summary>
    ValueTask<Result> SetAsAsync<TValue>(
        string @namespace,
        string? key,
        TValue value,
        JsonTypeInfo<TValue> typeInfo,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Moves one value to a different provider, withdrawing the copy it leaves behind.</summary>
    /// <param name="namespace">The namespace holding the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="toProvider">
    /// The provider to move to, or <see cref="IConfiguration.Plain"/> to store it unprotected from
    /// here on. Naming the provider the value already uses is refused rather than treated as a
    /// no-op, because in an operation this deliberate a silent success hides a caller mistake.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the move.</param>
    /// <returns>
    /// What the move did, including whether the source copy was withdrawn. A false
    /// <see cref="ConfigurationMove.SourceWithdrawn"/> means the value is safely readable at its new
    /// home but a copy survives at the old one; the same fact is logged as a warning.
    /// </returns>
    ValueTask<Result<ConfigurationMove>> MoveAsync(string @namespace, string key, string toProvider, CancellationToken cancellationToken = default);
}
