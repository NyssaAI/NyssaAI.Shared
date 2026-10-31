using System.Text.Json.Serialization.Metadata;

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
public interface IConfiguration : IConfigurationReader, IConfigurationWriter, IConfigurationMaintainer
{
}
