# NyssaAI.DbGateway

`NyssaAI.DbGateway` is a low-ceremony, Native AOT-compatible database gateway for .NET 10 that enables applications and AI tools to switch seamlessly between **SQLite** and **PostgreSQL** using reusable typed commands.

The library executes self-contained typed commands and queries (`IDbNonQuery`, `IDbCommand<TResult>`, `IDbQuery<TRow>`) across SQLite and PostgreSQL without ORM magic, reflection, or DI command-catalog registration ceremony. Commands own their SQL, bind parameters through dialect-aware coercion (`IParameterBinder`), and materialize results through dialect-normalizing readers (`IDataRecordReader`).

- **Reusable typed commands.** Self-contained expanded-body records executed directly on `IDbGateway` or `IDbTransaction` with zero DI command registration.
- **Automatic dialect type coercion.** Transparent bidirectional normalization for `Guid` (`TEXT` on SQLite vs. `uuid` on PostgreSQL), `bool` (`1`/`0` on SQLite vs. `boolean` on PostgreSQL), and `DateTimeOffset` (UTC ISO-8601 `TEXT` on SQLite vs. `timestamptz` on PostgreSQL).
- **Seamless SQLite-to-PostgreSQL switching.** Flip `DatabaseProvider` between `"sqlite"` and `"postgresql"` in configuration while keeping application use cases unchanged.
- **Automatic connection & transaction scopes.** Standalone operations manage their own connection lifetime; `ExecuteTransactionAsync` automatically commits on completion and rolls back on failure.
- **Paired & universal schema migrations.** Define a single `MigrationCatalog` with universal SQL steps or paired SQLite/PostgreSQL DDL steps pinned to exact SemVer releases.
- **Configuration integration.** Direct settings or encrypted persistence via `NyssaAI.Configuration`.
- **.NET 10 and Native AOT.** Zero runtime reflection, zero dynamic code generation, and no external ORM or interceptor dependencies.

## Quick Start

Register `IDbGateway` once during startup:

```csharp
using NyssaAI.DbGateway.Configuration;

services.AddNyssaDbGateway(new DbGatewayConfiguration
{
    DatabaseProvider = "sqlite",
    ConnectionString = "Data Source=tasks.db"
});
```

Switch to PostgreSQL by changing only the configuration:

```csharp
services.AddNyssaDbGateway(new DbGatewayConfiguration
{
    DatabaseProvider = "postgresql",
    ConnectionString = "Host=localhost;Database=tasks;Username=postgres;Password=secret"
});
```

## Reusable Typed Commands & Queries

Define an operation once and execute it anywhere in the application without registering command keys or factories in DI:

```csharp
using NyssaAI.DbGateway;
using NyssaAI.DbGateway.Commands;

public sealed record TaskItem
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required bool IsCompleted { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record TaskCreate : IDbNonQuery
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required bool IsCompleted { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public string GetSql(DbDialect dialect) =>
        "INSERT INTO tasks (id, title, is_completed, created_at) VALUES (@id, @title, @isCompleted, @createdAt)";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect)
    {
        parameters.Add("@id", Id);
        parameters.Add("@title", Title);
        parameters.Add("@isCompleted", IsCompleted);
        parameters.Add("@createdAt", CreatedAt);
    }
}

public sealed record TaskGetById : IDbCommand<TaskItem?>
{
    public required Guid Id { get; init; }

    public string GetSql(DbDialect dialect) =>
        "SELECT id, title, is_completed, created_at FROM tasks WHERE id = @id";

    public void BindParameters(IParameterBinder parameters, DbDialect dialect) =>
        parameters.Add("@id", Id);

    public async ValueTask<TaskItem?> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default)
    {
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new TaskItem
        {
            Id = reader.GetGuid(0),
            Title = reader.GetString(1),
            IsCompleted = reader.GetBoolean(2),
            CreatedAt = reader.GetDateTimeOffset(3)
        };
    }
}
```

Execute commands directly on `IDbGateway` or atomically inside `ExecuteTransactionAsync`:

```csharp
await gateway.ExecuteAsync(new TaskCreate
{
    Id = taskId,
    Title = "Ship DbGateway",
    IsCompleted = false,
    CreatedAt = DateTimeOffset.UtcNow
}, cancellationToken);

var task = await gateway.ExecuteAsync(new TaskGetById { Id = taskId }, cancellationToken);

await gateway.ExecuteTransactionAsync(async (tx, ct) =>
{
    await tx.ExecuteAsync(new TaskComplete { Id = taskId }, ct);
    await tx.ExecuteAsync(new AuditRecordInsert { TaskId = taskId, RecordedAt = DateTimeOffset.UtcNow }, ct);
}, cancellationToken);
```

## Paired & Universal Migrations

Define one `MigrationCatalog` that works across both SQLite and PostgreSQL:

```csharp
using NyssaAI.DbGateway.Migrations;

var migrations = MigrationCatalog.Create(
    steps:
    [
        MigrationStep.Paired(
            number: 1,
            sqliteUpSql: "CREATE TABLE tasks (id TEXT PRIMARY KEY, title TEXT NOT NULL, is_completed INTEGER NOT NULL, created_at TEXT NOT NULL)",
            postgresUpSql: "CREATE TABLE tasks (id UUID PRIMARY KEY, title TEXT NOT NULL, is_completed BOOLEAN NOT NULL, created_at TIMESTAMPTZ NOT NULL)",
            sqliteDownSql: "DROP TABLE tasks",
            postgresDownSql: "DROP TABLE tasks"),
        MigrationStep.Universal(
            number: 2,
            upSql: "ALTER TABLE tasks ADD COLUMN notes TEXT",
            downSql: "ALTER TABLE tasks DROP COLUMN notes")
    ],
    releases:
    [
        new() { Release = "0.0.0", Number = 0 },
        new() { Release = "1.0.0", Number = 1 },
        new() { Release = "1.1.0", Number = 2 }
    ]);

services.AddNyssaDbGateway(settings, migrations: migrations);

// Apply migrations explicitly when ready:
var report = await migrationRunner.MigrateAsync("1.1.0", cancellationToken);
```
