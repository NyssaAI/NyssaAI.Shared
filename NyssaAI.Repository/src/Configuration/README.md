# Repository configuration

`RepositoryConfiguration` contains `DatabaseProvider` and `ConnectionString`. The provider name is trimmed and normalized to lowercase; the connection string is preserved as supplied. Validation does not open a database. Its `ToString()` does not expose the connection string.

`AddNyssaRepository` accepts settings directly or a callback returning them. `AddNyssaRepositoryAsync` loads settings from an existing `NyssaAI.Configuration.IConfiguration` using source-generated JSON metadata, then delegates to the same registration method.

```csharp
services.AddNyssaRepository(settings, commands: commands);
```

For persisted settings:

```csharp
await configuration.SetRepositoryConfigurationAsync(settings, storageProvider: "aesLocal");
await services.AddNyssaRepositoryAsync(configuration, commands: commands);
```

The saved address is `nyssa.repository/default`. Supply `key` for another saved configuration. This storage namespace is retained from the earlier package so existing files remain addressable. There is no automatic data migration.

The first save uses plaintext storage unless a provider is supplied. Updates can inherit the existing destination, and changing it requires `IConfiguration.MoveAsync`. Loading never creates defaults. Stored changes apply to later registrations, while an existing provider keeps its settings snapshot.

Register once per service collection and await configuration loading before building the service provider. Invalid startup settings throw `ArgumentException`; `GetRepositoryConfigurationAsync` returns settings directly, while `SetRepositoryConfigurationAsync` returns a completion task. Both propagate Configuration exceptions unchanged; cancellation throws `OperationCanceledException`. Direct `IConfiguration` calls return values and throw on failure. The [configuration tutorials](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryConfigurationTutorials.cs) demonstrate encryption, reopening, and preserving protection.

Declare commands with `commands: commands => commands.Add(key, input => new Command(input))`, or use `services.AddNyssaRepositoryCommands(...)` before or after setup. The provider defaults to the configured database; the explicit overload is `commands.Add("sqlite", key, factory)`. Declarations accumulate and duplicate provider/operation pairs throw before changing services. See [registration examples](../../README.md#registering-commands).
