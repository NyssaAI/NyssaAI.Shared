using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace NyssaAI.Configuration;

/// <summary>Reads and lists plain or provider-backed configuration values.</summary>
public interface IConfigurationReader
{
    /// <summary>Reads one string value (e.g. json, xml, etc.).</summary>
    ValueTask<string> GetAsync(
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
}
