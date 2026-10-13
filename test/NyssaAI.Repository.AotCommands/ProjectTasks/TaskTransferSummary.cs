namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The persisted audit details of a project transfer.</summary>
public sealed record TaskTransferSummary
{
    /// <summary>Gets the transfer identifier.</summary>
    public required long TransferId { get; init; }

    /// <summary>Gets the source project identifier.</summary>
    public required long SourceProjectId { get; init; }

    /// <summary>Gets the destination project identifier.</summary>
    public required long DestinationProjectId { get; init; }

    /// <summary>Gets the number of tasks actually moved.</summary>
    public required long TasksMoved { get; init; }
}
