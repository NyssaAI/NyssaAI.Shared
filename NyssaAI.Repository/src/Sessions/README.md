# Sessions

`IRepository.OpenSessionAsync` opens one caller-owned connection. Use `await using` to release it after the work completes.

```csharp
await using var session = (await repository.OpenSessionAsync()).ThrowIfError();
var command = repository.CommandRegistry(ProjectTaskOperations.Get, taskId).ThrowIfError();
var task = (await session.ExecuteAsync(command)).ThrowIfError();
```

The command must have been resolved for the same provider. A session runs one operation at a time and rejects concurrent execution. Independent sessions can run concurrently.

A session does not begin a shared transaction. If a later command fails, earlier writes remain. Use a [transaction](../Transactions/README.md) for work that must succeed together. Buffered results remain usable after the scope is disposed.
