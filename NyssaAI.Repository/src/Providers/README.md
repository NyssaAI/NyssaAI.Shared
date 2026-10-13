# Database providers

`IRepositoryProvider` identifies the selected database and opens independent connections. SQLite and PostgreSQL are registered automatically. The service container owns the provider; callers own the connections or scopes they open.

To add another provider, implement `IRepositoryProvider` and pass a registration to `AddNyssaRepository`:

```csharp
services.AddNyssaRepository(settings, providers: [providerRegistration], commands: commands);
```

`RepositoryProviderRegistration.Prepare` validates settings without I/O and returns a factory capturing those settings. The factory creates a fresh provider without fallible initialization. `OpenConnectionAsync` performs resource acquisition and returns either an open connection or a sanitized failure, cleaning up any partially opened resources.

Provider names are normalized to lowercase. Registration rejects missing and duplicate names, including attempts to replace the built-in providers. Custom providers retain the library's command resolution, sessions, and transactions. Built-in migrations support SQLite and PostgreSQL only.

The SQLite provider uses `Microsoft.Data.Sqlite`. The PostgreSQL provider creates one Npgsql data source lazily and opens connections from it. Dispose all active scopes before disposing the container.

See the [custom provider](../../../test/NyssaAI.Repository.UseCaseTests/fixtures/ApplicationProvider.cs) and [registration tutorial](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryProviderTutorials.cs).
