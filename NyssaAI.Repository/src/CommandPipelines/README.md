# Command pipelines

A pipeline describes dependent commands on one existing session or transaction. Constructing it does not resolve commands or access the database.

```csharp
var result = await transaction
    .Pipeline(ProjectTaskOperations.MoveOpen, move)
    .Then(ProjectTaskOperations.RecordTransfer, summary => summary)
    .ExecuteAsync(cancellationToken);
```

Each `Then` receives the preceding successful result. Its parameter factory returns parameters directly and throws to reject invalid input. A failure stops subsequent commands. On a transaction, failure also prevents commit.

The caller owns the scope and decides when to commit or dispose it. A pipeline on a session is not atomic. Executing a pipeline again repeats its commands, including writes.

See the [pipeline tutorials](../../../test/NyssaAI.Repository.UseCaseTests/CommandPipelineTutorials.cs) for successful dependent writes and rollback after failure.
