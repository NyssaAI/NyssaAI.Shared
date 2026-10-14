namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The values needed to create an open task.</summary>
public sealed record TaskCreateParameters
{
    /// <summary>Gets the task identifier.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the project identifier.</summary>
    public required long ProjectId { get; init; }

    /// <summary>Gets the task title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the optional task note.</summary>
    public required string? Note { get; init; }
}
