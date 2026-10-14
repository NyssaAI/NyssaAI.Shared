namespace NyssaAI.Configuration.Recovery;

/// <summary>Cancellation after an operation started, preserving progress and the caller's token.</summary>
public sealed class ConfigurationOperationCanceledException : OperationCanceledException
{
    internal ConfigurationOperationCanceledException(ConfigurationOperationProgress progress, ConfigurationRecoveryHandle? handle, CancellationToken token)
        : base("The configuration operation was canceled. Inspect Progress before retrying.", token)
    {
        Progress = progress;
        RecoveryHandle = handle;
    }
    /// <summary>The work observed before cancellation.</summary>
    public ConfigurationOperationProgress Progress { get; }
    /// <summary>The protected state for explicit continuation, when available.</summary>
    public ConfigurationRecoveryHandle? RecoveryHandle { get; }
}
