namespace NyssaAI.Repository.AotCommands.ProjectTasks;

/// <summary>A compact application DTO with an explicit conversion from task details.</summary>
public sealed record TaskCard
{
    /// <summary>Gets the task identifier.</summary>
    public required long TaskId { get; init; }

    /// <summary>Gets the heading displayed on the card.</summary>
    public required string Heading { get; init; }

    /// <summary>Projects task details into the fields needed by a task card.</summary>
    public static explicit operator TaskCard(TaskDetails task) => new()
    {
        TaskId = task.Id,
        Heading = task.Title
    };
}
