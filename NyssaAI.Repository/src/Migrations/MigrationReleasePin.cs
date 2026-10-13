namespace NyssaAI.Repository.Migrations;

/// <summary>Maps an exact application semantic version to its schema number.</summary>
public sealed record MigrationReleasePin
{
    /// <summary>Gets the full case-sensitive SemVer identity, including build metadata.</summary>
    public required string Release { get; init; }
    /// <summary>Gets the target schema number; zero means no applied migrations.</summary>
    public required long Number { get; init; }
}
