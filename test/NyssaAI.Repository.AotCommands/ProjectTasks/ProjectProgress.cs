namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>Aggregate task counts for one project.</summary>
public sealed record ProjectProgress
{
    /// <summary>Gets the project identifier.</summary>
    public required long ProjectId { get; init; }

    /// <summary>Gets the total task count.</summary>
    public required long TotalTasks { get; init; }

    /// <summary>Gets the completed task count.</summary>
    public required long CompletedTasks { get; init; }

    /// <summary>Gets the open task count.</summary>
    public required long RemainingTasks { get; init; }
}
