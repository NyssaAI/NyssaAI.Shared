namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>The project whose tasks should be queried.</summary>
public sealed record ProjectTaskParameters
{
    /// <summary>Gets the project identifier.</summary>
    public required long ProjectId { get; init; }
}
