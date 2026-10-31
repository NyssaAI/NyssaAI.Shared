# DbGateway tutorials

These executable use-case tests demonstrate `NyssaAI.DbGateway` workflows across both SQLite and PostgreSQL:

| Example | What it demonstrates |
|---|---|
| [Task workflows](../../test/use_case/DbGatewayTutorials.cs) | Create, read, update, list, dialect-divergent identity return, parameter/row coercion (`Guid`, `bool`, `DateTimeOffset`), and transactions across SQLite and PostgreSQL |
| [Paired migrations](../../test/use_case/DbGatewayMigrationTutorials.cs) | Universal and paired SQLite/PostgreSQL schema migrations, release pins, upgrades, and downgrades |
| [Protected settings](../../test/use_case/DbGatewayConfigurationTutorials.cs) | Saving, loading, and registering encrypted connection settings with `NyssaAI.Configuration` |
| [Custom providers](../../test/use_case/DbGatewayProviderTutorials.cs) | Custom `IDbProvider` registration and execution |
