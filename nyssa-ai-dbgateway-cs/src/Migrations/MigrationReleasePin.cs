namespace NyssaAI.DbGateway.Migrations;

/// <summary>MigrationReleasePin maps an exact semantic version release string to a target migration step number (or 0 for an empty schema).</summary>
public sealed record MigrationReleasePin
{
    /// <summary>Gets the exact full SemVer release identifier.</summary>
    public required string Release { get; init; }

    /// <summary>Gets the target migration step number (0 for an empty schema).</summary>
    public required long Number { get; init; }
}
