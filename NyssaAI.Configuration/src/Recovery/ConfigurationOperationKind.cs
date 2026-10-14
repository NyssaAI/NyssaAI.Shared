namespace NyssaAI.Configuration.Recovery;

/// <summary>The configuration workflow whose progress is reported.</summary>
public enum ConfigurationOperationKind
{
    /// <summary>Move one entry between storage providers.</summary>
    Move,
    /// <summary>Replace all entries in a namespace.</summary>
    Replace,
    /// <summary>Merge supplied entries into a namespace.</summary>
    Merge,
}
