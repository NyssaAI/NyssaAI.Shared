# NyssaAI.Repository

`NyssaAI.Repository` helps .NET applications, including AI solutions, move between SQLite and PostgreSQL while retaining the same typed operation contracts.

The library executes typed database commands across multiple database providers. It supports the queries and operations your application needs, beyond standard CRUD. Your application defines and registers its commands and configures the database provider. You execute command instances through a session for independent operations or a transaction for work that must succeed together. The library manages connections and transaction lifetimes, supports chaining dependent operations, and returns values directly, throwing on failure.

- **Typed commands.** Application-owned SQL, parameters, and result types.
- **Database providers.** SQLite, PostgreSQL, and custom implementations behind shared operation contracts.
- **Sessions and transactions.** Managed connections, explicit commits, and rollback of uncommitted work on disposal.
- **Command pipelines.** Chain typed operations and stop on failure.
- **Failure handling.** Exceptions report failures; failed transactions cannot commit.
- **Schema migrations.** Explicit, versioned migrations with history validation.
- **Configuration.** Direct settings or `NyssaAI.Configuration`, with optional credential protection.
- **Registration.** Typed command declarations inside or outside repository setup, without assembly scanning.
- **.NET 10 and Native AOT.** AOT support when application commands and dependencies are compatible.

## Easy setup, consistent access

Register the database connection and the commands your application uses. Here, `ProjectTaskOperations` is the [task example](../test/NyssaAI.Repository.AotCommands/ProjectTasks/ProjectTaskOperations.cs) included with the tutorials.

```csharp
services.AddNyssaRepository(new RepositoryConfiguration
{
    DatabaseProvider = "sqlite",
    ConnectionString = "Data Source=tasks.db"
});

services.AddNyssaRepositoryCommands(commands => commands
    .Add(ProjectTaskOperations.Get, parameters => new TaskGet(parameters))
    .Add(ProjectTaskOperations.Rename, parameters => new TaskRename(parameters)));
```

The registration extension and settings type are in `NyssaAI.Repository.Configuration`. Use `"postgresql"` and a PostgreSQL connection string to select the other built-in provider. Provider names are trimmed and normalized to lowercase.

- **Keep SQL with the operation it performs.** A command contains the SQL, parameter binding, and result handling for one operation. Different providers can register different commands under the same typed key.
- **Use a session for ordinary reads and writes.** Each session opens its own connection. Dispose it when the work is complete.
- **Use a transaction for changes that belong together.** Commands share one connection and transaction. Commit explicitly after all work succeeds.
- **Run migrations when the application is ready.** Registration does not open a database or change its schema. Migrations run only when you call them.

## IRepository - The main interface

Inject `IRepository` into the classes that need database access. A typed command key defines the parameters and the complete result type. Execute a command directly on a session or transaction by supplying its key and parameters, or resolve a command first via `CommandRegistry`.

```csharp
await using (var session = await repository.OpenSessionAsync())
{
    var task = await session.ExecuteAsync(ProjectTaskOperations.Get, taskId);
}
```

Here, `taskId` is an already-populated parameter object. The key determines that the result is a `TaskDetails` object; no cast or separate generic type argument is needed.

```text
IRepository
|-- CommandRegistry       Select a typed command for the configured provider
|-- OpenSessionAsync      Open a caller-owned connection
`-- OpenTransactionAsync  Open a caller-owned connection and transaction
```

Commands can return an affected-row count, a scalar, a DTO, a buffered list, or an application-defined result containing several values. The command decides what an empty result means. For example, the task lookup throws a `KeyNotFoundException`, while the task list returns an empty list.

The public API returns commands, scopes, DTOs, scalars, and collections directly. Operations with no return value, such as commit and rollback, return `Task`. No `Result` wrapper or unwrapping call is required.

Repository uses ordinary .NET exceptions throughout:

| Failure | Exception |
|---|---|
| Null required argument | `ArgumentNullException` |
| Invalid argument or settings | `ArgumentException` |
| Duplicate registration, missing configured provider, invalid factory output | `InvalidOperationException` |
| Missing command or release pin | `KeyNotFoundException` |
| Command incompatible with the scope's provider | `ArgumentException` |
| Disposed scope or provider | `ObjectDisposedException` |
| Busy, completed, or failed transaction; invalid migration history or lock contention | `InvalidOperationException` |
| Unsupported migration provider | `NotSupportedException` |
| Caller cancellation | `OperationCanceledException` |

Application, data-mapping, and driver exceptions propagate with their original diagnostics. Catch the exceptions your application can handle at its boundary. Repository-authored validation messages omit SQL, parameter values, and credentials. Dependency exception messages are diagnostic information and must not be displayed or logged as though they were sanitized.

The stored-settings integration propagates Configuration exceptions unchanged, including its own failure and recovery information. Repository has no direct Result dependency; Configuration still brings its independent failure model and transitive dependency.

## Registering commands

Use the same `RepositoryCommandBuilder` syntax inside `AddNyssaRepository` or separately on the service collection:

```csharp
services.AddNyssaRepositoryCommands(commands =>
{
    commands.Add(ProjectTaskOperations.Get, parameters => new TaskGet(parameters));
    commands.Add(ProjectTaskOperations.Rename, parameters => new TaskRename(parameters));
});

services.AddNyssaRepository(repositorySettings);
```

Separate declarations can come before or after repository setup. Complete registration before building the container. You can split declarations across feature-specific extension methods and combine them with inline declarations. Each call adds to the command catalog; duplicate operation names for the same provider throw during registration, before that call changes the service collection. If settings have not yet selected a provider, collisions between default and explicitly named registrations are checked when the repository is configured.

The typed key supplies both generic types. Omitting the provider uses the configured database. Specify a provider only when the declaration must target one explicitly:

```csharp
services.AddNyssaRepositoryCommands(commands => commands
    .Add("sqlite", ProjectTaskOperations.Get, parameters => new TaskGet(parameters))
    .Add("postgresql", ProjectTaskOperations.Get, parameters => new TaskGet(parameters)));
```

These tutorial commands support both providers. Applications with different SQL can supply different command implementations under the same key.

Declaration callbacks run once during registration. Command factories run each time `CommandRegistry` selects a command and should create a fresh instance. Throw from the factory to reject invalid parameters:

```csharp
commands.Add(ProjectTaskOperations.Get, parameters =>
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parameters.Id);
    return new TaskGet(parameters);
});
```

Registrations are snapshotted: retaining and modifying a builder later does not alter its installed catalog, and modifying the service collection does not alter an already-built container. The callback syntax works with direct settings, a settings factory, and `AddNyssaRepositoryAsync`. Explicit `RepoCommandRegistration` lists remain available; `Create` now returns the registration directly and accepts a factory returning the command directly.

## Sessions and transactions

A session keeps one connection open for the work inside its `await using` block. Its commands run without a shared database transaction. Earlier writes remain if a later command fails.

A transaction adds an explicit commit:

```csharp
await using (var transaction = await repository.OpenTransactionAsync())
{
    await transaction.ExecuteAsync(ProjectTaskOperations.Rename, rename);
    await transaction.CommitAsync();
}
```

Disposing an uncommitted transaction attempts rollback and releases its resources. A connection loss or database failure can prevent rollback from completing. A command failure, failed resolution through `transaction.ExecuteAsync(key, parameters)`, or rejected operation through the transaction prevents a later commit, including an unexpected exception from command code. A failed command lookup performed separately through `IRepository.CommandRegistry` has not reached the transaction.

Use each scope sequentially. Concurrent operations on the same session or transaction throw `InvalidOperationException`. Independent scopes can run concurrently. The application owns and disposes its scopes; the service container owns the registered provider and should be disposed after those scopes finish.

## Command pipelines

A pipeline describes commands that depend on earlier results. It uses a session or transaction you have already opened.

```csharp
var transfer = await transaction
    .Pipeline(ProjectTaskOperations.MoveOpen, move)
    .Then(ProjectTaskOperations.RecordTransfer, summary => summary)
    .ExecuteAsync();
await transaction.CommitAsync();
```

Import `NyssaAI.Repository.CommandPipelines` for the `Pipeline` extension. Building the pipeline performs no database work. `ExecuteAsync` resolves and runs each command in order, stopping on the first failure. A dependent parameter factory returns the next input directly and throws if it cannot create valid input. A pipeline exception leaves its transaction unable to commit, even when the caller catches it.

The pipeline does not open, commit, or dispose its scope. Calling `ExecuteAsync` again runs the commands again. Use a transaction when the whole sequence must succeed or roll back together.

## Configuration

The simplest registration supplies `RepositoryConfiguration` directly. A callback can also return the settings:

```csharp
services.AddNyssaRepository(() => repositorySettings, commands: commands);
```

You can load those settings through [`NyssaAI.Configuration`](../NyssaAI.Configuration/README.md) when the application needs persisted settings or protected credentials:

```csharp
await configuration.SetRepositoryConfigurationAsync(repositorySettings, storageProvider: "aesLocal");
await services.AddNyssaRepositoryAsync(configuration, commands: commands);
```

`configuration` is an existing `IConfiguration`. The first call stores the complete settings DTO at `nyssa.repository/default`. A named key allows several saved connection settings. New entries default to plaintext. To protect them, supply `"aesLocal"` or a configured vault name. Later saves can omit it to keep the existing protection.

Registration reads and validates one settings snapshot before adding services. It does not create missing settings or retain the configuration service. Changing a stored value affects future registrations; it does not replace an active provider. Direct registration requires no configuration host or files.

## Switching a deployment between SQLite and PostgreSQL

An application can include commands for both built-in providers and select the database through deployment settings. Supply the provider name and its connection string at startup:

```csharp
services.AddNyssaRepository(new RepositoryConfiguration
{
    DatabaseProvider = databaseProvider,
    ConnectionString = connectionString
});
```

Here, `databaseProvider` and `connectionString` come from the application's deployment configuration:

| Deployment | `DatabaseProvider` | `ConnectionString` |
|---|---|---|
| SQLite | `"sqlite"` | `"Data Source=tasks.db"` |
| PostgreSQL | `"postgresql"` | A PostgreSQL connection string containing the server, database, and authentication settings |

When a command's SQL and result handling work with both databases, register it once without naming a provider. The registration uses the configured database:

```csharp
services.AddNyssaRepositoryCommands(commands => commands
    .Add(ProjectTaskOperations.Get, parameters => new TaskGet(parameters))
    .Add(ProjectTaskOperations.Rename, parameters => new TaskRename(parameters)));
```

You can also register a delegate directly without writing an `IRepoCommand` class:

```csharp
services.AddNyssaRepositoryCommands(commands => commands
    .Add(ProjectTaskOperations.CountOpen, async (parameters, connection, transaction, cancellationToken) =>
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM tutorial_tasks WHERE project_id = @projectId AND is_completed = 0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@projectId";
        parameter.Value = parameters.ProjectId;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }));
```

When the SQL differs, register an implementation for each provider under the same typed operation key:

```csharp
services.AddNyssaRepositoryCommands(commands => commands
    .Add("sqlite", ProjectTaskOperations.Get,
        parameters => new TaskGetSqlite(parameters))
    .Add("postgresql", ProjectTaskOperations.Get,
        parameters => new TaskGetPostgres(parameters)));
```

`TaskGetSqlite` and `TaskGetPostgres` represent application-defined command classes. Repository automatically selects the implementation matching `DatabaseProvider`; callers continue using `ProjectTaskOperations.Get`. For each operation, use either a shared registration or explicit provider registrations. Combining both for the selected provider produces a duplicate-registration error.

To switch an existing deployment, prepare the target database and its [schema migrations](#schema-migrations), transfer any existing data the application needs, update the provider and connection settings, and restart the application. Each repository instance uses one configured database, and an existing service container retains its original settings. Repository selects registered commands automatically; it does not translate SQL or transfer data between databases.

## Startup and architecture

The library starts in the following order:

1. Registration validates the settings, provider registrations, and supplied command and migration lists. It prepares the selected provider without opening a connection.
2. The service container creates the provider and `RepositoryClient` when they are first resolved. Both are singletons within that container.
3. `CommandRegistry` finds the registration matching the configured provider and typed operation key, invokes its factory with the supplied parameters, and creates a command. The factory owns application-specific parameter validation.
4. Opening a session or transaction obtains its own connection from the provider. The scope executes commands and releases the connection when disposed.
5. If requested, `IMigrationRunner` opens a separate connection and applies the registered migration catalog.

```text
Application
    |
IRepository
    |
RepositoryClient
    |
    +-- SQLite provider
    +-- PostgreSQL provider
    `-- Application provider
```

[`RepositoryClient`](src/Client/RepositoryClient.cs) implements `IRepository`. It selects commands from the registered command list and opens the scopes that execute them. [`IRepositoryProvider`](src/Providers/IRepositoryProvider.cs) is the provider contract: it identifies the database and opens connections. SQLite uses `Microsoft.Data.Sqlite`; PostgreSQL uses a lazily created Npgsql data source.

The application owns its command implementations. A command borrows the scope's connection and transaction, binds parameters, and returns the result. It must not retain, dispose, or commit those resources. The included commands use Dapper with generated bindings; the repository library itself has no Dapper dependency.

## Providers and commands

Register a custom provider with a `RepositoryProviderRegistration` and pass it in the `providers` argument. Its preparation function validates settings and returns a factory. The factory creates the provider; opening a connection handles resource acquisition and reports connection failures. A custom provider uses the same client, sessions, and transactions as the built-in providers.

Command registrations associate a provider name and `RepoCommandKey<TParameters, TResult>` with a typed factory. Duplicate provider/operation pairs are rejected during registration. Resolution throws for missing or incompatible registrations and invalid command parameters. It does not discover command types automatically.

For complete examples, see [command authoring](src/Commands/README.md), [provider registration](src/Providers/README.md), and the [custom-provider tutorial](../test/NyssaAI.Repository.UseCaseTests/RepositoryProviderTutorials.cs).

## Schema migrations

Migrations are compiled SQL definitions in a `MigrationCatalog`. Each step has a consecutive number, an `UpSql` statement, and an optional `CommandTimeoutSeconds` override (defaulting to the catalog's `commandTimeoutSeconds`, which defaults to 30 seconds; `0` means infinite); reversible steps also have `DownSql`. Release pins map exact semantic versions, such as `"1.2.0"`, to a schema number.

Pass the catalog in the `migrations` registration argument, then invoke the separate `IMigrationRunner` service explicitly before serving database traffic:

```csharp
var migrations = serviceProvider.GetRequiredService<IMigrationRunner>();
var report = await migrations.MigrateAsync("1.2.0");
```

The runner stores applied step numbers and fingerprints in `nyssa_repository_migrations`. It verifies that history matches the compiled catalog before changing the schema. An upgrade runs forward to the pinned number; a downgrade requires reverse SQL for every step being removed. Schema changes and history updates run together in one transaction.

Migration catalogs support the built-in SQLite and PostgreSQL providers. Competing migration attempts fail with `InvalidOperationException`. The runner rejects unsupported transaction-changing SQL; migration definitions are trusted application code, not untrusted input. See [migration behavior and examples](src/Migrations/README.md).

## Native AOT and cancellation

Command and provider registrations are explicit, and stored settings use source-generated JSON metadata. Native AOT applications must also compile their own command code and database dependencies for their target platform. The [native smoke application](../test/NyssaAI.Repository.AotSmoke/README.md) exercises concrete Dapper.AOT commands, transactions, pipelines, and migrations. This is not a guarantee that every driver feature or custom command supports AOT.

Pass cancellation tokens to scope opening, command execution, pipeline execution, and migrations. When cancellation is observed, it throws `OperationCanceledException`; built-in provider opening preserves the caller token. Application cancellation exceptions retain their own token, and unrelated database failures and timeouts remain their original exceptions. Npgsql 10.0.3 can defer cancellation during a stalled startup read until its connection timeout; that failure remains an `NpgsqlException` with a `TimeoutException` cause. With an unlimited connection timeout, such a startup read can remain pending. Pool-wait and command cancellation are separately exercised; Repository does not abandon an opening connection or relabel a driver timeout.

Any operation failure in a transaction prevents further execution and commit. Subsequent rejection throws `InvalidOperationException` with the first failure as `InnerException`. Explicit rollback remains available with a fresh token; disposal attempts rollback using `CancellationToken.None`. Ordinary sessions remain usable after an operation fails. Disposed scopes throw `ObjectDisposedException`; completed scopes throw `InvalidOperationException`.

This exception contract replaces the former Result categories. Consumers catching Repository `ResultException` must handle the relevant .NET or driver exception instead; direct success return values and registration syntax remain unchanged.

Cancellation also depends on the provider and command implementation. PostgreSQL supports canceling in-flight commands. SQLite execution is synchronous within its async API, so cancellation cannot be relied on to interrupt an already-running statement promptly.

## Tutorials and tests

The [registration tutorial](../test/NyssaAI.Repository.UseCaseTests/RepositoryRegistrationTutorials.cs) exercises inline, separate, settings-factory, and stored-settings declarations. The [tutorial index](docs/tutorials/README.md) links runnable examples of ordinary reads and writes, transactions, dependent commands, protected settings, custom providers, and migrations.

Unit tests live in `test/NyssaAI.Repository.Tests`. Tutorials and the native smoke application live under the monorepo's root `test` directory. PostgreSQL tests require `NYSSAAI_REPOSITORY_POSTGRES` to point to an isolated test database and fail if it is missing. SQLite tests use isolated databases. See the [monorepo README](../README.md) for the complete test runner.
