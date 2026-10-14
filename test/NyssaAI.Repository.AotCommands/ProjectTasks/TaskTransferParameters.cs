namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The identity and endpoints of a task transfer.</summary>
public sealed record TaskTransferParameters
{
    /// <summary>Gets the transfer identifier.</summary>
    public required long TransferId { get; init; }

    /// <summary>Gets the source project identifier.</summary>
    public required long SourceProjectId { get; init; }

    /// <summary>Gets the destination project identifier.</summary>
    public required long DestinationProjectId { get; init; }
}
