namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>The application's configuration shape, serialized without a library-owned schema.</summary>
public sealed record ExportPreferences
{
    public required string Format { get; init; }
    public required bool IncludeHistory { get; init; }
}
