# Typed commands

A `RepoCommandKey<TParameters, TResult>` names an operation and fixes its input and output types. `RepoCommandRegistration.Create` (or `RepositoryCommandBuilder.Add`) connects that key to a provider and either a factory returning an `IRepoCommand<TParameters, TResult>` or an inline `RepoCommandHandler<TParameters, TResult>` delegate.

```csharp
var registration = RepoCommandRegistration.Create("sqlite", ProjectTaskOperations.Get,
    parameters => new TaskGet(parameters));

var inlineRegistration = RepoCommandRegistration.Create("sqlite", ProjectTaskOperations.CountOpen,
    async (parameters, connection, transaction, cancellationToken) =>
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM tutorial_tasks WHERE project_id = @projectId AND is_completed = 0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@projectId";
        parameter.Value = parameters.ProjectId;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    });
```

Pass registrations to `AddNyssaRepository(..., commands: registrations)`. Use the same key for provider-specific implementations when the caller's input and output remain the same. Names are case-sensitive. Duplicate provider/operation pairs throw during registration, before services change. The [shared declaration syntax](../../README.md#registering-commands) also supports omitting the default provider and registering separately from repository setup.

A command supplies `Key`, `Parameters`, and `ExecuteCoreAsync`. That method receives a borrowed `DbConnection`, an optional `DbTransaction`, and a cancellation token. It returns `Task<TResult>` with the complete value, and throws on failure. It must honor the supplied transaction and must not close, retain, or commit the borrowed resources.

The complete [TaskGet command](../../../test/NyssaAI.Repository.AotCommands/ProjectTasks/TaskGet.cs) shows a parameterized query, a buffered DTO, and a `KeyNotFoundException`. [ProjectTaskOperations](../../../test/NyssaAI.Repository.AotCommands/ProjectTasks/ProjectTaskOperations.cs) shows the typed keys and factories.

The examples use Dapper and Dapper.AOT. The project containing concrete Dapper calls enables generated interceptors and adds `[module: Dapper.DapperAot]`; enabling it only in the application entry assembly is insufficient when commands live elsewhere. Cancellation is carried in the generated parameter binding. See the [command project](../../../test/NyssaAI.Repository.AotCommands/NyssaAI.Repository.AotCommands.csproj) and [binding example](../../../test/NyssaAI.Repository.AotCommands/Tasks/TaskBinding.cs).

The repository itself does not require Dapper. A command can use another ADO.NET-compatible implementation if it respects the same resource ownership, transaction, result, and cancellation contracts.
