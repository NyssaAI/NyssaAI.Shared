namespace NyssaAI.DbGateway.Migrations;

/// <summary>MigrationStep defines a numbered schema migration step supporting universal or dialect-paired SQL for SQLite and PostgreSQL.</summary>
public sealed record MigrationStep
{
    /// <summary>Gets the 1-based consecutive step number.</summary>
    public required long Number { get; init; }

    /// <summary>Gets the SQLite upgrade SQL statement.</summary>
    public required string SqliteUpSql { get; init; }

    /// <summary>Gets the PostgreSQL upgrade SQL statement.</summary>
    public required string PostgresUpSql { get; init; }

    /// <summary>Gets the optional SQLite downgrade SQL statement.</summary>
    public string? SqliteDownSql { get; init; }

    /// <summary>Gets the optional PostgreSQL downgrade SQL statement.</summary>
    public string? PostgresDownSql { get; init; }

    /// <summary>Gets the optional step-specific command timeout in seconds (0 for infinite).</summary>
    public int? CommandTimeoutSeconds { get; init; }

    /// <summary>Creates a universal migration step whose SQL executes identically on both SQLite and PostgreSQL.</summary>
    /// <param name="number">The 1-based step number.</param>
    /// <param name="upSql">The portable upgrade SQL.</param>
    /// <param name="downSql">The optional portable downgrade SQL.</param>
    /// <param name="commandTimeoutSeconds">The optional step command timeout in seconds.</param>
    /// <returns>A configured <see cref="MigrationStep"/>.</returns>
    public static MigrationStep Universal(
        long number,
        string upSql,
        string? downSql = null,
        int? commandTimeoutSeconds = null) =>
        new()
        {
            Number = number,
            SqliteUpSql = upSql,
            PostgresUpSql = upSql,
            SqliteDownSql = downSql,
            PostgresDownSql = downSql,
            CommandTimeoutSeconds = commandTimeoutSeconds
        };

    /// <summary>Creates a paired migration step with dialect-specific SQL for SQLite and PostgreSQL.</summary>
    /// <param name="number">The 1-based step number.</param>
    /// <param name="sqliteUpSql">The SQLite upgrade SQL.</param>
    /// <param name="postgresUpSql">The PostgreSQL upgrade SQL.</param>
    /// <param name="sqliteDownSql">The optional SQLite downgrade SQL.</param>
    /// <param name="postgresDownSql">The optional PostgreSQL downgrade SQL.</param>
    /// <param name="commandTimeoutSeconds">The optional step command timeout in seconds.</param>
    /// <returns>A configured <see cref="MigrationStep"/>.</returns>
    public static MigrationStep Paired(
        long number,
        string sqliteUpSql,
        string postgresUpSql,
        string? sqliteDownSql = null,
        string? postgresDownSql = null,
        int? commandTimeoutSeconds = null) =>
        new()
        {
            Number = number,
            SqliteUpSql = sqliteUpSql,
            PostgresUpSql = postgresUpSql,
            SqliteDownSql = sqliteDownSql,
            PostgresDownSql = postgresDownSql,
            CommandTimeoutSeconds = commandTimeoutSeconds
        };

    internal string GetUpSql(DbDialect dialect) =>
        dialect == DbDialect.Sqlite ? SqliteUpSql : PostgresUpSql;

    internal string? GetDownSql(DbDialect dialect) =>
        dialect == DbDialect.Sqlite ? SqliteDownSql : PostgresDownSql;
}
