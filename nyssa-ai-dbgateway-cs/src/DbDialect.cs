namespace NyssaAI.DbGateway;

/// <summary>Identifies the SQL dialect and storage engine backing a database gateway.</summary>
public enum DbDialect
{
    /// <summary>Embedded SQLite database engine.</summary>
    Sqlite = 0,

    /// <summary>Server-grade PostgreSQL database engine.</summary>
    PostgreSql = 1
}
