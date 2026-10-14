# Schema migrations

`MigrationCatalog.Create` validates and snapshots SQL definitions for SQLite or PostgreSQL. Definitions use consecutive step numbers starting at one. Release pins associate exact full semantic versions with a step number; zero represents an empty application schema.

```csharp
var catalog = MigrationCatalog.Create("sqlite",
    [new() { Number = 1, UpSql = "CREATE TABLE tasks(id BIGINT PRIMARY KEY)", DownSql = "DROP TABLE tasks" }],
    [new() { Release = "1.0.0", Number = 1 }]);
services.AddNyssaRepository(settings, migrations: [catalog]);
```

Registration does not run the migrations. Resolve `IMigrationRunner` and call it before serving database traffic:

```csharp
var report = await migrations.MigrateAsync("1.0.0");
```

The runner acquires a database lock, validates the stored history, applies the required SQL, and commits schema and history together. SQLite uses an immediate write transaction; PostgreSQL uses a transaction-scoped advisory lock. A competing migration attempt throws `InvalidOperationException`.

The `nyssa_repository_migrations` table records step numbers and fingerprints of the compiled SQL. Existing history must match a prefix of the catalog. Changing an already-applied definition is rejected. A downgrade is checked in full before any reverse SQL runs, and every removed step needs `DownSql`.

Release matching is exact and case-sensitive, including prerelease and build metadata. Asking for an unregistered release throws a `KeyNotFoundException`. Applying the current release succeeds with no changes.

Definitions are trusted application code. The validator rejects unsupported transaction-control statements and other SQL that would break atomic execution; it is not a security boundary for arbitrary SQL. Custom providers do not inherit built-in migration support.

The [migration tutorial](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryMigrationAotTutorials.cs) demonstrates deployment, repeated deployment, exact release matching, and downgrade on both providers.
