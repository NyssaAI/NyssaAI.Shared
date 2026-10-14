namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskReport A complete buffered application result constructed from two supported generated queries.</summary>
public sealed record TaskReport
{
    /// <summary>Gets the scalar count from the first query.</summary>
    public required long Count { get; init; }
    /// <summary>Gets the rows from the second query.</summary>
    public IReadOnlyList<TaskRow> Rows { get; init; } = [];
}
