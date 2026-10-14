using NyssaAI.Configuration.CloudKeyVaults;
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

internal sealed record ConfigurationRecoveryStep
{
    public required string Provider { get; init; }
    public required CloudKeyVaultOperation Operation { get; init; }
    public bool Local { get; init; }
    public bool IsJsonValue { get; init; }
    public CloudKeyVaultPreparation? Preparation { get; init; }
    public string? TargetReference { get; init; }
    public ConfigurationValue? Output { get; init; }
    public ConfigurationValue? Source { get; init; }
    public ConfigurationStepState State { get; init; }
    public override string ToString() => "ConfigurationRecoveryStep { protected }";
}
