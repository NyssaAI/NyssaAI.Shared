namespace NyssaAI.Repository.AotCommands.Tasks;
/// <summary>TaskParameters Values supplied by an ordinary caller; cancellation remains a separate API argument.</summary>
public sealed record TaskParameters
{
    /// <summary>Gets the numeric fixture row key.</summary>
    public required long Id { get; init; }
    /// <summary>Gets the exact bound title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets an optional note.</summary>
    public required string? Note { get; init; }
}
