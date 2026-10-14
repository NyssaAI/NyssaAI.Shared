namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The identity and replacement title of a task.</summary>
public sealed record TaskRenameParameters
{
    /// <summary>Gets the task identifier.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the replacement title.</summary>
    public required string Title { get; init; }
}
