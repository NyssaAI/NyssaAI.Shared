namespace NyssaAI.Repository.Migrations;

/// <summary>Describes one successfully committed migration run, including a no-op.</summary>
public sealed record MigrationReport
{
    /// <summary>Gets the verified schema number before execution.</summary>
    public required long FromNumber { get; init; }
    /// <summary>Gets the pinned schema number after commit.</summary>
    public required long ToNumber { get; init; }
    /// <summary>Gets the number of executed up or down steps.</summary>
    public required int AppliedCount { get; init; }
}
