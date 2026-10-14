using NyssaAI.Results;

namespace NyssaAI.Configuration.Recovery;

/// <summary>A failed configuration operation with progress that survives the Result boundary.</summary>
public sealed class ConfigurationOperationException : ResultException
{
    internal ConfigurationOperationException(IReadOnlyList<Error> errors, ConfigurationOperationProgress progress, ConfigurationRecoveryHandle? handle)
        : base(errors)
    {
        Progress = progress;
        RecoveryHandle = handle;
    }
    /// <summary>The work observed before failure.</summary>
    public ConfigurationOperationProgress Progress { get; }
    /// <summary>The protected state for explicit continuation, when available.</summary>
    public ConfigurationRecoveryHandle? RecoveryHandle { get; }
    /// <summary>A value-free message; provider error details remain available through Errors.</summary>
    public override string Message => "The configuration operation did not finish. Inspect Progress before retrying.";
}
