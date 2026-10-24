namespace NyssaAI.Repository.Migrations;

/// <summary>Compiled provider-specific transactional SQL for one contiguous schema step.</summary>
/// <remarks>SQL is trusted application code. Do not include transaction control, nontransactional operations, or modify the runner's history table.</remarks>
public sealed record MigrationDefinition
{
    /// <summary>Gets the positive step number; catalogs start at one without gaps.</summary>
    public required long Number { get; init; }
    /// <summary>Gets the SQL moving from the preceding step to this step.</summary>
    public required string UpSql { get; init; }
    /// <summary>Gets the SQL returning to the preceding step; null declares an irreversible step.</summary>
    public string? DownSql { get; init; }
    /// <summary>Gets the optional command timeout in seconds for this migration step (0 for infinite; null uses the catalog default).</summary>
    public int? CommandTimeoutSeconds { get; init; }
}
