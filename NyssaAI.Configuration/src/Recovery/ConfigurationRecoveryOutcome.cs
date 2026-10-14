namespace NyssaAI.Configuration.Recovery;

/// <summary>The latest progress and optional replacement handle after explicit recovery.</summary>
public sealed record ConfigurationRecoveryOutcome
{
    /// <summary>Whether the workflow completed or needs attention.</summary>
    public required ConfigurationRecoveryStatus Status { get; init; }
    /// <summary>The current payload-free progress.</summary>
    public required ConfigurationOperationProgress Progress { get; init; }
    /// <summary>A replacement handle while safely recoverable work remains.</summary>
    public ConfigurationRecoveryHandle? RecoveryHandle { get; init; }
}
