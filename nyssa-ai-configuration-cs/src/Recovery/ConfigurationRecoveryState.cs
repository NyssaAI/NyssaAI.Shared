using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Recovery;

/// <summary>The private, encrypted replay envelope; never a diagnostic receipt.</summary>
internal sealed record ConfigurationRecoveryState
{
    public bool Conflicted { get; init; }
    public int Version { get; init; } = 1;
    public required Guid OperationId { get; init; }
    public required string Namespace { get; init; }
    public required ConfigurationOperationKind Kind { get; init; }
    public required Dictionary<string, ConfigurationValue> Original { get; init; }
    public required Dictionary<string, ConfigurationValue> Proposed { get; init; }
    public required List<ConfigurationRecoveryStep> Steps { get; init; }
    public ConfigurationPublicationState Publication { get; init; }
    public override string ToString() => "ConfigurationRecoveryState { protected }";
}
