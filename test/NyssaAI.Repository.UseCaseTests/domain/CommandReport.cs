namespace NyssaAI.Repository.UseCaseTests.domain;

public sealed record CommandReport
{
    public IReadOnlyList<CommandRow> Rows { get; init; } = [];

    public required int Total { get; init; }
}
