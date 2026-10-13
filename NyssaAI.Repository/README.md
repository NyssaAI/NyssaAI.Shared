# NyssaAI.Repository

`NyssaAI.Repository` runs typed database commands for .NET applications. Application code uses [`IRepository`](src/IRepository.cs) to select commands and open sessions or transactions. SQLite and PostgreSQL providers are included. Your application supplies the commands, SQL, and result types.

The library manages connections and transaction lifetimes, reports expected failures through `Result`, and supports explicit schema migrations. Commands and providers are registered in code, with no assembly scanning. The library targets .NET 10 and supports Native AOT when the application's commands and dependencies also support it.

## Easy setup, consistent access

Register the database connection and the commands your application uses. Here, `ProjectTaskOperations` is the [task example](../test/NyssaAI.Repository.AotCommands/ProjectTasks/ProjectTaskOperations.cs) included with the tutorials.

```csharp
services.AddNyssaRepository(new RepositoryConfiguration
{
    DatabaseProvider = "sqlite",
    ConnectionString = "Data Source=tasks.db"
}, commands: ProjectTaskOperations.Register("sqlite"));
```

The registration extension and settings type are in `NyssaAI.Repository.Configuration`. Use `"postgresql"` and a PostgreSQL connection string to select the other built-in provider. Provider names are trimmed and normalized to lowercase.

- **Keep SQL with the operation it performs.** A command contains the SQL, parameter binding, and result handling for one operation. Different providers can register different commands under the same typed key.
- **Use a session for ordinary reads and writes.** Each session opens its own connection. Dispose it when the work is complete.
- **Use a transaction for changes that belong together.** Commands share one connection and transaction. Commit explicitly after all work succeeds.
- **Run migrations when the application is ready.** Registration does not open a database or change its schema. Migrations run only when you call them.

## IRepository - The main interface

Inject `IRepository` into the classes that need database access. A typed command key defines the parameters and the complete result type. Selecting a command does not run it.

```csharp
await using var session = (await repository.OpenSessionAsync()).ThrowIfError();
var command = repository.CommandRegistry(ProjectTaskOperations.Get, taskId).ThrowIfError();
var task = (await session.ExecuteAsync(command)).ThrowIfError();
```

Here, `taskId` is an already-populated parameter object. The key determines that the result is a `TaskDetails` object; no cast or separate generic type argument is needed.

```text
IRepository
|-- CommandRegistry       Select a typed command for the configured provider
|-- OpenSessionAsync      Open a caller-owned connection
`-- OpenTransactionAsync  Open a caller-owned connection and transaction
```

Commands can return an affected-row count, a scalar, a DTO, a buffered list, or an application-defined result containing several values. The command decides what an empty result means. For example, the task lookup returns `NotFound`, while the task list returns an empty list.

Operations return [`Result` or `Result<T>`](../NyssaAI.Result/README.md). Check the result before using its value or running dependent work. These short examples use `ThrowIfError()` to stop on failure. Invalid registration fails at startup with `ResultException`.

## Sessions and transactions

A session keeps one connection open for the work inside its `await using` block. Its commands run without a shared database transaction. Earlier writes remain if a later command fails.

A transaction adds an explicit commit:

```csharp
await using var transaction = (await repository.OpenTransactionAsync()).ThrowIfError();
var command = repository.CommandRegistry(ProjectTaskOperations.Rename, rename).ThrowIfError();
(await transaction.ExecuteAsync(command)).ThrowIfError();
(await transaction.CommitAsync()).ThrowIfError();
```

Disposing a transaction without a successful commit rolls it back. A command failure or rejected operation through the transaction prevents a later commit, including an unexpected exception from command code. A failed command lookup performed separately through `IRepository` has not reached the transaction; check that result before continuing.

Use each scope sequentially. Concurrent operations on the same session or transaction return a conflict. Independent scopes can run concurrently. The application owns and disposes its scopes; the service container owns the registered provider and should be disposed after those scopes finish.

## Command pipelines

A pipeline describes commands that depend on earlier results. It uses a session or transaction you have already opened.

```csharp
var transfer = await transaction
    .Pipeline(repository, ProjectTaskOperations.MoveOpen, move)
    .Then(ProjectTaskOperations.RecordTransfer, summary => summary)
    .ExecuteAsync();
transfer.ThrowIfError();
(await transaction.CommitAsync()).ThrowIfError();
```

Import `NyssaAI.Repository.CommandPipelines` for the `Pipeline` extension. Building the pipeline performs no database work. `ExecuteAsync` resolves and runs each command in order, stopping on the first failure. A dependent parameter factory can return a `Result` when it needs to validate the previous result.

The pipeline does not open, commit, or dispose its scope. Calling `ExecuteAsync` again runs the commands again. Use a transaction when the whole sequence must succeed or roll back together.

## Configuration

The simplest registration supplies `RepositoryConfiguration` directly. A callback can also return the settings:

```csharp
services.AddNyssaRepository(() => repositorySettings, commands: commands);
```

You can load those settings through [`NyssaAI.Configuration`](../NyssaAI.Configuration/README.md) when the application needs persisted settings or protected credentials:

```csharp
(await configuration.SetRepositoryConfigurationAsync(repositorySettings, storageProvider: "aesLocal")).ThrowIfError();
await services.AddNyssaRepositoryAsync(configuration, commands: commands);
```

`configuration` is an existing `IConfiguration`. The first call stores the complete settings DTO at `nyssa.repository/default`. A named key allows several saved connection settings. New entries require an explicit storage provider: use `IConfiguration.Plain`, `"aesLocal"`, or a configured vault name. Later saves can omit it to keep the existing protection.

Registration reads and validates one settings snapshot before adding services. It does not create missing settings or retain the configuration service. Changing a stored value affects future registrations; it does not replace an active provider. Direct registration requires no configuration host or files.

## Startup and architecture

The library starts in the following order:

1. Registration validates the settings, provider registrations, and supplied command and migration lists. It prepares the selected provider without opening a connection.
2. The service container creates the provider and `RepositoryClient` when they are first resolved. Both are singletons within that container.
3. `CommandRegistry` finds the registration matching the configured provider and typed operation key, validates its parameters, and creates a command.
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

Command registrations associate a provider name and `RepoCommandKey<TParameters, TResult>` with a typed factory. Resolution reports missing, duplicate, or incompatible registrations through `Result`. It does not discover command types automatically.

For complete examples, see [command authoring](src/Commands/README.md), [provider registration](src/Providers/README.md), and the [custom-provider tutorial](../test/NyssaAI.Repository.UseCaseTests/RepositoryProviderTutorials.cs).

## Schema migrations

Migrations are compiled SQL definitions in a `MigrationCatalog`. Each step has a consecutive number and an `UpSql` statement; reversible steps also have `DownSql`. Release pins map exact semantic versions, such as `"1.2.0"`, to a schema number.

Pass the catalog in the `migrations` registration argument, then invoke the separate `IMigrationRunner` service explicitly before serving database traffic:

```csharp
var migrations = serviceProvider.GetRequiredService<IMigrationRunner>();
var report = (await migrations.MigrateAsync("1.2.0")).ThrowIfError();
```

The runner stores applied step numbers and fingerprints in `nyssa_repository_migrations`. It verifies that history matches the compiled catalog before changing the schema. An upgrade runs forward to the pinned number; a downgrade requires reverse SQL for every step being removed. Schema changes and history updates run together in one transaction.

Migration catalogs support the built-in SQLite and PostgreSQL providers. Competing migration attempts fail with a conflict. The runner rejects unsupported transaction-changing SQL; migration definitions are trusted application code, not untrusted input. See [migration behavior and examples](src/Migrations/README.md).

## Native AOT and cancellation

Command and provider registrations are explicit, and stored settings use source-generated JSON metadata. Native AOT applications must also compile their own command code and database dependencies for their target platform. The [native smoke application](../test/NyssaAI.Repository.AotSmoke/README.md) exercises concrete Dapper.AOT commands, transactions, pipelines, and migrations. This is not a guarantee that every driver feature or custom command supports AOT.

Pass cancellation tokens to scope opening, command execution, pipeline execution, and migrations. Recognized database failures and cancellation become `Result` failures with sanitized messages. Unexpected exceptions from application command code remain visible and leave an active transaction unable to commit.

Cancellation also depends on the provider and command implementation. PostgreSQL supports canceling in-flight commands. SQLite execution is synchronous within its async API, so cancellation cannot be relied on to interrupt an already-running statement promptly.

## Tutorials and tests

The [tutorial index](docs/tutorials/README.md) links runnable examples of ordinary reads and writes, transactions, dependent commands, protected settings, custom providers, and migrations.

Unit tests live in `test/NyssaAI.Repository.Tests`. Tutorials and the native smoke application live under the monorepo's root `test` directory. PostgreSQL tests require `NYSSAAI_REPOSITORY_POSTGRES` to point to an isolated test database and fail if it is missing. SQLite tests use isolated databases. See the [monorepo README](../README.md) for the complete test runner.
