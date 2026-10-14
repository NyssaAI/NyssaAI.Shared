using NyssaAI.Configuration.CloudKeyVaults;

namespace NyssaAI.Configuration.Recovery;

/// <summary>Payload-free diagnostics for one configuration mutation.</summary>
public sealed record ConfigurationStepProgress
{
    /// <summary>The stable request identity used by the provider.</summary>
    public required Guid OperationId { get; init; }
    /// <summary>The affected configuration key.</summary>
    public required string Key { get; init; }
    /// <summary>The provider name; empty means default local storage.</summary>
    public required string Provider { get; init; }
    /// <summary>The requested mutation.</summary>
    public required CloudKeyVaultAction Action { get; init; }
    /// <summary>The observed effect of the request.</summary>
    public required ConfigurationStepState State { get; init; }
    /// <summary>A provider-supplied, non-secret reference for manual reconciliation, when available.</summary>
    public string? TargetReference { get; init; }
}
