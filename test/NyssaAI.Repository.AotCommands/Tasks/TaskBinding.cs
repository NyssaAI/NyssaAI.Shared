namespace NyssaAI.Repository.AotCommands.Tasks;
internal sealed record TaskBinding
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public required string? Note { get; init; }
    public required CancellationToken CancellationToken { get; init; }
    internal static TaskBinding From(TaskParameters parameters, CancellationToken cancellationToken)
        => new() { Id = parameters.Id, Title = parameters.Title, Note = parameters.Note, CancellationToken = cancellationToken };
}
