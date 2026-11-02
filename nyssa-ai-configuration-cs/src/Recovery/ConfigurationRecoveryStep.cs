using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Recovery;

/// <summary>Describes a single recovery step within a multi-step configuration operation.</summary>
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
