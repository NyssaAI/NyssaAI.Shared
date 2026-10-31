namespace NyssaAI.DbGateway.Migrations;

/// <summary>MigrationReport summarizes the completed schema migration transition.</summary>
public sealed record MigrationReport
{
    /// <summary>Gets the schema step number before the migration ran.</summary>
    public required long FromNumber { get; init; }

    /// <summary>Gets the schema step number after the migration completed.</summary>
    public required long ToNumber { get; init; }

    /// <summary>Gets the number of upgrade or downgrade steps executed.</summary>
    public required int AppliedCount { get; init; }
}
