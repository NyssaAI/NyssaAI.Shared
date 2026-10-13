using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration;

/// <summary>Reads and changes plain or provider-backed configuration values.</summary>
/// <remarks>
/// <para>
/// The contract reads, writes, removes, and lists values in a namespace and key hierarchy, including
/// JSON values deserialized into a caller-supplied type. Operations return values directly and
/// throw on failure. Store and provider failures throw <see cref="NyssaAI.Results.ResultException"/>;
/// cancellation throws <see cref="OperationCanceledException"/>.
/// Directory discovery and registration
/// happen before opening; the manifest decides them.
/// </para>
/// <para>
/// Each generic member comes as a pair. The reflection-based overload is the convenient one and is
/// annotated so a trimmed or Native AOT build reports it; the overload taking a
/// <see cref="JsonTypeInfo{T}"/> is the one such a build uses, because a caller-supplied type
/// cannot be enumerated into a source-generated context at this package's compile time.
/// </para>
/// </remarks>
public interface IConfiguration
{
    /// <summary>Names plain, unprotected storage when writing a value that has no entry yet.</summary>
    /// <remarks>
    /// Keyed writes require an explicit provider for new entries. The namespace-settings overloads
    /// use plain storage for new properties and preserve existing providers. The package never infers protection from a key's name.
    /// </remarks>
    public const string Plain = "";

    /// <summary>Reads one string value (e.g. json, xml, etc.).</summary>
    ValueTask<string> GetAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one string value, storing it through the provider that will serve it.</summary>
    /// <param name="namespace">The namespace to hold the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="provider">
    /// Where the value is stored. Null inherits the destination the existing entry already records,
    /// and fails on a key that has none yet rather than guessing. Pass <see cref="Plain"/> to store
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
    ValueTask SetAsync(
        string @namespace,
        string key,
        string value,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one value, succeeding when it is already absent.</summary>
    ValueTask RemoveAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the keys in one namespace in ordinal order.</summary>
    ValueTask<IReadOnlyList<string>> ListAsync(
        string @namespace,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves the namespace entries and assembles them into a settings object.</summary>
    /// <returns>The deserialized settings. An empty namespace throws; no default object is created.</returns>
    /// <remarks>Uses each entry's recorded provider. Legacy namespaceSettings objects remain readable. Reading does not create a default object.</remarks>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        CancellationToken cancellationToken = default);

    /// <summary>Assembles namespace entries using a source-generated JSON type description.</summary>
    /// <returns>The deserialized settings. An empty namespace throws; no default object is created.</returns>
    /// <remarks>Uses each entry's recorded provider. Legacy namespaceSettings objects remain readable. Reading does not create a default object.</remarks>
    ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        CancellationToken cancellationToken = default);

    /// <summary>Reads and deserializes one value with the reflection-based JSON serializer.</summary>
    /// <remarks>
    /// String reads accept unencoded text from SetAsync or namespace settings and JSON-encoded
    /// strings from keyed SetAsAsync writes. The entry's JSON metadata determines which form is
    /// stored; text resembling JSON is preserved. Other types deserialize the stored JSON text.
    /// </remarks>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Reads and deserializes one value with a source-generated JSON type description.</summary>
    /// <remarks>
    /// String reads use the entry's JSON metadata to distinguish unencoded text from JSON strings,
    /// then apply the supplied type description. Other types deserialize the stored JSON text.
    /// </remarks>
    ValueTask<TValue> GetAsAsync<TValue>(
        string @namespace,
        string key,
        JsonTypeInfo<TValue> typeInfo,
        CancellationToken cancellationToken = default);

    /// <summary>Stores each top-level settings property as a namespace entry.</summary>
    /// <remarks>
    /// Preserves existing providers; new properties use plaintext. Omitted keys are left unchanged.
    /// Nested objects remain together. Remote writes are not atomic across providers.
    /// A legacy protected namespaceSettings object must be explicitly moved to plaintext before migration.
    /// </remarks>
    /// <returns>A task that completes when the write succeeds; serialization, storage, and provider-conflict failures throw.</returns>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        TValue value,
        CancellationToken cancellationToken = default);

    /// <summary>Stores settings properties using a source-generated JSON type description.</summary>
    /// <remarks>
    /// Preserves existing providers; new properties use plaintext. Omitted keys are left unchanged.
    /// Nested objects remain together. Remote writes are not atomic across providers.
    /// A legacy protected namespaceSettings object must be explicitly moved to plaintext before migration.
    /// Type information precedes the value to avoid ambiguity with keyed writes when TValue is string.
    /// </remarks>
    /// <returns>A task that completes when the write succeeds; serialization, storage, and provider-conflict failures throw.</returns>
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        TValue value,
        CancellationToken cancellationToken = default);

    /// <summary>Serializes and writes one value with the reflection-based JSON serializer.</summary>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Serializes and writes one value with a source-generated JSON type description.</summary>
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        JsonTypeInfo<TValue> typeInfo,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Moves one value to a different provider, withdrawing the copy it leaves behind.</summary>
    /// <param name="namespace">The namespace holding the value.</param>
    /// <param name="key">The key of the value within the namespace.</param>
    /// <param name="toProvider">
    /// The provider to move to, or <see cref="Plain"/> to store it unprotected from here on. Naming
    /// the provider the value already uses is refused rather than treated as a no-op, because in an
    /// operation this deliberate a silent success hides a caller mistake.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the move.</param>
    /// <returns>
    /// What the move did, including whether the source copy was withdrawn. A false
    /// <see cref="ConfigurationMove.SourceWithdrawn"/> means the value is safely readable at its new
    /// home but a copy survives at the old one; the same fact is logged as a warning.
    /// </returns>
    ValueTask<ConfigurationMove> MoveAsync(
        string @namespace,
        string key,
        string toProvider,
        CancellationToken cancellationToken = default);
}
