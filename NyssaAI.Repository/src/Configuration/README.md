# Repository configuration

`RepositoryConfiguration` contains `DatabaseProvider` and `ConnectionString`. The provider name is trimmed and normalized to lowercase; the connection string is preserved as supplied. Validation does not open a database. Its `ToString()` does not expose the connection string.

`AddNyssaRepository` accepts settings directly or a callback returning them. `AddNyssaRepositoryAsync` loads settings from an existing `NyssaAI.Configuration.IConfiguration` using source-generated JSON metadata, then delegates to the same registration method.

```csharp
services.AddNyssaRepository(settings, commands: commands);
```

For persisted settings:

```csharp
(await configuration.SetRepositoryConfigurationAsync(settings, storageProvider: "aesLocal")).ThrowIfError();
await services.AddNyssaRepositoryAsync(configuration, commands: commands);
```

The saved address is `nyssa.repository/default`. Supply `key` for another saved configuration. This storage namespace is retained from the earlier package so existing files remain addressable. There is no automatic data migration.

The first save requires an explicit storage provider. Updates can inherit the existing destination, and changing it requires `IConfiguration.MoveAsync`. Loading never creates defaults. Stored changes apply to later registrations, while an existing provider keeps its settings snapshot.

Register once per service collection and await configuration loading before building the service provider. Invalid startup settings throw `ResultException`; ordinary settings reads and writes return `Result` failures. The [configuration tutorials](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryConfigurationTutorials.cs) demonstrate encryption, reopening, and preserving protection.
