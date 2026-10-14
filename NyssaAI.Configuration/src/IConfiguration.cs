using NyssaAI.Configuration.Recovery;
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
    /// <summary>Reads one string value (e.g. json, xml, etc.).</summary>
    ValueTask<string> GetAsync(
        string @namespace,
        string key,
        CancellationToken cancellationToken = default);

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
    ValueTask SetAsync(
        string @namespace,
        string key,
        string value,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one value through its provider and removes its local entry.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry within the namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the removal.</param>
    /// <returns>
    /// Whether the provider still holds a remote copy, including one retained by soft deletion.
    /// Local-only and already absent entries return a false
    /// <see cref="ConfigurationRemoval.RemoteValueRemains"/> flag. An absent entry does not trigger
    /// a search for previously untracked remote copies.
    /// </returns>
    /// <remarks>
    /// Provider retention is a successful removal with the flag set to true. A provider failure
    /// throws and leaves the local entry in place for retry. If the provider deletes the value but
    /// saving the local document fails, the local entry may still point at the deleted value;
    /// retrying removal is safe. Namespace replacement only prunes local entries; use this method
    /// for provider deletion before replacing a namespace.
    /// </remarks>
    ValueTask<ConfigurationRemoval> RemoveAsync(
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

    /// <summary>Replaces the namespace contents with the serialized settings properties.</summary>
    /// <remarks>
    /// Included keys preserve their providers; new keys use default storage. Omitted keys are removed
    /// from the local namespace, leaving any remote secrets in place. An empty object clears all entries.
    /// Use MergeSettingsAsync for partial updates that preserve omitted keys. Only serialized properties
    /// are included; a serialized null is a value, not an omission. Nested objects are replaced as whole values.
    /// Remote writes are not atomic across providers. Non-object values replace the namespace with the legacy single entry.
    /// A legacy protected namespaceSettings object must be explicitly moved to plaintext before migration.
    /// </remarks>
    /// <returns>A task that completes when the write succeeds; serialization, storage, and provider-conflict failures throw.</returns>
    /// <example>
    /// <code>
    /// await configuration.SetAsAsync("mail", new { Host = "smtp.example.test", Port = 587 });
    /// await configuration.SetAsAsync("mail", new { Host = "smtp.new.test" });
    /// // Port is removed; the namespace now contains only Host.
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        TValue value,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the namespace contents using a source-generated JSON type description.</summary>
    /// <remarks>
    /// Included keys preserve their providers; new keys use default storage. Properties omitted by the
    /// serializer are removed from the local namespace, leaving any remote secrets in place. An empty
    /// object clears all entries. Use MergeSettingsAsync for partial updates. Serialized nulls are values;
    /// nested objects are replaced as whole values. Remote writes are not atomic across providers.
    /// Non-object values replace the namespace with the legacy single entry.
    /// A legacy protected namespaceSettings object must be explicitly moved to plaintext before migration.
    /// Type information precedes the value to avoid ambiguity with keyed writes when TValue is string.
    /// </remarks>
    /// <returns>A task that completes when the write succeeds; serialization, storage, and provider-conflict failures throw.</returns>
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        TValue value,
        CancellationToken cancellationToken = default);

    /// <summary>Merges serialized top-level settings properties into a namespace, preserving omitted keys.</summary>
    /// <remarks>
    /// Requires a JSON object. Included properties replace their entire values, including nested objects;
    /// this is not a recursive merge. Serialized nulls are stored as values. Existing providers are preserved
    /// and new keys use default storage. An empty object leaves existing settings unchanged.
    /// Use namespace SetAsAsync to replace all entries. Remote writes are not atomic across providers.
    /// A plaintext legacy namespaceSettings object is expanded into entries before merging; a protected
    /// legacy object must be moved to default storage first. Non-object legacy settings cannot be merged.
    /// </remarks>
    /// <example>
    /// <code>
    /// await configuration.SetAsAsync("mail", new { Host = "smtp.example.test", Port = 587 });
    /// await configuration.MergeSettingsAsync("mail", new { Host = "smtp.new.test" });
    /// // Host changes; Port remains 587.
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask MergeSettingsAsync<TValue>(
        string @namespace,
        TValue value,
        CancellationToken cancellationToken = default);

    /// <summary>Merges settings using a source-generated JSON type description, preserving omitted keys.</summary>
    /// <remarks>
    /// Requires a JSON object. Each included serialized property replaces its whole value; nested objects
    /// are not recursively merged and serialized nulls are stored as values. Existing providers are preserved;
    /// new keys use default storage. An empty object leaves existing settings unchanged.
    /// Use namespace SetAsAsync for replacement. Remote writes are not atomic across providers.
    /// Plaintext legacy objects are expanded before merging; protected legacy objects must be moved to
    /// default storage first. Non-object legacy settings cannot be merged.
    /// </remarks>
    ValueTask MergeSettingsAsync<TValue>(
        string @namespace,
        JsonTypeInfo<TValue> typeInfo,
        TValue value,
        CancellationToken cancellationToken = default);

    /// <summary>Serializes and replaces the complete value at one key; other namespace entries are unchanged.</summary>
    /// <remarks>Omitted object members are removed from this value. Omitting the provider uses default storage for a new entry and preserves it for an existing entry. Use MoveAsync to change providers.</remarks>
    [RequiresUnreferencedCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    [RequiresDynamicCode("TValue is caller-supplied; Native AOT callers use the JsonTypeInfo overload.")]
    ValueTask SetAsAsync<TValue>(
        string @namespace,
        string key,
        TValue value,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the complete value at one key using source-generated JSON; other namespace entries are unchanged.</summary>
    /// <remarks>Object members omitted by the serializer are removed from this value. Omitting the provider uses default storage for a new entry and preserves it for an existing entry. Use MoveAsync to change providers.</remarks>
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
    /// The destination provider, or null to return to default storage. Moving to the destination
    /// the value already uses is refused rather than treated as a no-op.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the move.</param>
    /// <returns>
    /// What the move did, including whether the source copy was withdrawn. A false
    /// <see cref="ConfigurationMove.SourceWithdrawn"/> means the value is safely readable at its new
    /// home but a copy survives at the old one, including after soft deletion; the same fact is logged as a warning.
    /// </returns>
    ValueTask<ConfigurationMove> MoveAsync(
        string @namespace,
        string key,
        string? toProvider = null,
        CancellationToken cancellationToken = default);
    /// <summary>Continues unfinished move or namespace work captured by a recovery handle.</summary>
    /// <remarks>Validates the handle, checks for changed local and remote values, reconciles uncertain requests, and executes only safely unfinished work. Unsupported providers require manual reconciliation. Retention is terminal; this never purges or rolls back.</remarks>
    /// <param name="handle">A handle from an outcome or exception, optionally imported after restart.</param>
    /// <param name="cancellationToken">Cancels this attempt without discarding its progress.</param>
    ValueTask<ConfigurationRecoveryOutcome> RecoverAsync(ConfigurationRecoveryHandle handle, CancellationToken cancellationToken = default);

}
