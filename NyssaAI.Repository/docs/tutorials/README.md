# Repository tutorials

These executable tests show application calls after their fixtures have prepared an isolated host and database. They keep setup separate so the operation and its result are easy to follow.

| Example | What it demonstrates |
|---|---|
| [Command registration](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryRegistrationTutorials.cs) | Inline and separate declarations, provider defaults, startup validation, and snapshots |
| [Task workflows](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryTutorials.cs) | Create, read, update, list, count, transfer, and transactions |
| [Command pipelines](../../../test/NyssaAI.Repository.UseCaseTests/CommandPipelineTutorials.cs) | Dependent writes and failure rollback |
| [Protected settings](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryConfigurationTutorials.cs) | Save, reopen, and preserve encrypted connection settings |
| [Custom providers](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryProviderTutorials.cs) | Application provider registration and configuration integration |
| [Typed commands](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryAotTutorials.cs) | Concrete generated command execution on both providers |
| [Migrations](../../../test/NyssaAI.Repository.UseCaseTests/RepositoryMigrationAotTutorials.cs) | Exact release pins, upgrades, and downgrades |

The [AotCommands project](../../../test/NyssaAI.Repository.AotCommands/NyssaAI.Repository.AotCommands.csproj) contains the concrete commands used by tutorials and the [native smoke application](../../../test/NyssaAI.Repository.AotSmoke/README.md). Each Dapper call is compiled in the assembly that enables Dapper.AOT.

Run the use-case project with an isolated PostgreSQL database configured in `NYSSAAI_REPOSITORY_POSTGRES`. PostgreSQL cases fail when that variable is absent. SQLite cases create their own files. The complete monorepo test runner also runs package-local unit tests.

Repository tutorials assert direct values and ordinary exceptions. Driver diagnostics propagate unchanged; a failed transaction rejects commit with its first failure as the cause. Configuration integration retains Configuration's separate exception contract.
