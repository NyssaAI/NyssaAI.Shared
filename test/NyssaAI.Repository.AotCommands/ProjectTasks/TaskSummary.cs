namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The fields needed to display a task in a list.</summary>
public sealed record TaskSummary
{
    /// <summary>Gets the task identifier.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the task title.</summary>
    public required string Title { get; init; }
}
