# Transactions

`IRepository.OpenTransactionAsync` opens a connection and starts a database transaction. It exposes the same command execution as a session, with explicit commit and rollback methods.

```csharp
await using var transaction = (await repository.OpenTransactionAsync()).ThrowIfError();
(await transaction.ExecuteAsync(command)).ThrowIfError();
(await transaction.CommitAsync()).ThrowIfError();
```

A failed command, canceled operation, concurrent operation, or failed pipeline through the transaction prevents subsequent execution and commit. Ignoring the returned failure does not make it safe to commit. Unexpected exceptions from command or pipeline code also leave the transaction unable to commit.

A separately failed `repository.CommandRegistry(...)` lookup has not executed through the transaction. Check it before continuing, or use a pipeline to include resolution in the transaction's failure tracking.

Disposal rolls back uncommitted work and releases the connection. `RollbackAsync` allows earlier explicit rollback. A completed transaction rejects more work. Complete all scopes before disposing the service container.

External work such as sending messages is outside the database transaction. Save an outbox record in the transaction and publish it separately after commit when delivery must follow the database change.
