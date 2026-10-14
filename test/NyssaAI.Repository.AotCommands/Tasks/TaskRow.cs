namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskRow A concrete generated row shape with required init-only properties.</summary>
public sealed record TaskRow
{
    /// <summary>Gets the stored fixture row key.</summary>
    public required long Id { get; init; }
    /// <summary>Gets the stored title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets the nullable stored note.</summary>
    public required string? Note { get; init; }
}
