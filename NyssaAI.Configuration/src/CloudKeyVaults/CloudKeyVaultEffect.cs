using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>Provider evidence about an execution or read-only reconciliation.</summary>
/// <remarks>Applied writes must return their original pointer. Failed proves no effect and a still-valid retry condition. Unknown proves neither. Retained is terminal deletion acknowledgement, never permission to purge.</remarks>
public sealed record CloudKeyVaultEffect
{
    /// <summary>Applied, Failed, Unknown, Conflict, Retained or Manual. Pending is not a provider result.</summary>
    public required ConfigurationStepState State { get; init; }
    /// <summary>The original output of an applied write, verified to still identify that value.</summary>
    public ConfigurationPointer? Pointer { get; init; }
}
