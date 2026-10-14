namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The identity of a task to read or complete.</summary>
public sealed record TaskIdParameters
{
    /// <summary>Gets the task identifier.</summary>
    public required long Id { get; init; }
}
