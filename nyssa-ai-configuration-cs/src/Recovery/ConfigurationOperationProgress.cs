namespace NyssaAI.Configuration.Recovery;

/// <summary>A snapshot of completed, uncertain, and unfinished configuration work; contains no values or replay tokens.</summary>
public sealed record ConfigurationOperationProgress
{
    /// <summary>Whether continuation is available or manual reconciliation is required.</summary>
    public ConfigurationRecoveryStatus Recovery { get; init; }
    /// <summary>The stable identity of the whole workflow.</summary>
    public required Guid OperationId { get; init; }
    /// <summary>The configuration namespace.</summary>
    public required string Namespace { get; init; }
    /// <summary>The original workflow.</summary>
    public required ConfigurationOperationKind Kind { get; init; }
    /// <summary>An immutable snapshot of the steps in execution order.</summary>
    public required IReadOnlyList<ConfigurationStepProgress> Steps { get; init; }
    /// <summary>Local publication, distinct from remote success.</summary>
    public required ConfigurationPublicationState Publication { get; init; }
}
