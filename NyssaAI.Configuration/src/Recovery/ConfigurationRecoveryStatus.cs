namespace NyssaAI.Configuration.Recovery;

/// <summary>The result of explicitly continuing a configuration workflow.</summary>
public enum ConfigurationRecoveryStatus
{
    /// <summary>All work completed, including acknowledged provider retention.</summary>
    Completed,
    /// <summary>Work remains and another explicit attempt may be useful.</summary>
    Unfinished,
    /// <summary>A newer value or different target prevented continuation.</summary>
    Conflict,
    /// <summary>Safe continuation cannot be established by the provider.</summary>
    ManualReconciliation,
}
