namespace NyssaAI.Configuration.Recovery;

/// <summary>Evidence about a single operation, independent of local publication.</summary>
public enum ConfigurationStepState
{
    /// <summary>No request has been dispatched.</summary>
    Pending,
    /// <summary>The provider acknowledged completion.</summary>
    Applied,
    /// <summary>The provider proved that the request had no effect.</summary>
    Failed,
    /// <summary>The effect cannot currently be established.</summary>
    Unknown,
    /// <summary>A newer value or changed target prevents continuation.</summary>
    Conflict,
    /// <summary>The provider acknowledged deletion but retains a copy under its policy.</summary>
    Retained,
    /// <summary>The provider cannot establish safe continuation.</summary>
    Manual,
}
