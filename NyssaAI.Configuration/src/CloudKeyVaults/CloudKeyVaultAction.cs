namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>A mutation supported by the optional vault recovery protocol.</summary>
public enum CloudKeyVaultAction
{
    /// <summary>Store the frozen value.</summary>
    Write,
    /// <summary>Delete the exact source identified during preparation.</summary>
    Remove,
}
