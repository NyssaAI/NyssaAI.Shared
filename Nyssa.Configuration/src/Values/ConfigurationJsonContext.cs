using NyssaAI.Configuration.Contracts;
using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// The source-generated JSON context for what this feature persists: one namespace document's
/// values, keyed by their key.
/// </summary>
/// <remarks>
/// Source generation keeps the AOT analyzer clean without a reflection fallback. The options here
/// reproduce what the reflection-based call sites used before, so a document written by either is
/// byte-identical: indented, with <see cref="ConfigurationValue"/>'s explicit property names.
/// Dictionary keys are never renamed. Sibling features own the contexts for the shapes they
/// persist, so nothing here names a type from another feature.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, ConfigurationValue>))]
internal sealed partial class ConfigurationJsonContext : JsonSerializerContext
{
}
