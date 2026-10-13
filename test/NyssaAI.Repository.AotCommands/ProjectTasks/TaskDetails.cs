namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The task details returned to application callers.</summary>
public sealed record TaskDetails
{
    /// <summary>Gets the task identifier.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the owning project identifier.</summary>
    public required long ProjectId { get; init; }

    /// <summary>Gets the task title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the optional task note.</summary>
    public required string? Note { get; init; }

    /// <summary>Gets whether the task is complete.</summary>
    public required bool IsCompleted { get; init; }
}
