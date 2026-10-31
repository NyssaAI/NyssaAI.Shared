using NyssaAI.DbGateway.Migrations;
using NyssaAI.DbGateway.Providers;

namespace NyssaAI.DbGateway.UseCaseTests.Fixtures;

public sealed class DatabaseWorkspace : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("nyssa-dbgateway-tutorial-").FullName;

    public string DirectoryPath => _directory;

    public string ConnectionString(string provider) =>
        provider == "sqlite"
            ? $"Data Source={Path.Join(_directory, "tutorial.db")};Pooling=False"
            : Environment.GetEnvironmentVariable("NYSSAAI_DBGATEWAY_POSTGRES")
                ?? Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES")
                ?? throw new InvalidOperationException("Set NYSSAAI_DBGATEWAY_POSTGRES to an isolated test database.");

    public static MigrationCatalog TaskCatalog { get; } = MigrationCatalog.Create(
        steps:
        [
            MigrationStep.Paired(
                number: 1,
                sqliteUpSql: """
                    CREATE TABLE tutorial_tasks (
                        id TEXT PRIMARY KEY,
                        project_id BIGINT NOT NULL,
                        title TEXT NOT NULL,
                        is_completed INTEGER NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    CREATE TABLE tutorial_audit (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        task_id TEXT NOT NULL,
                        action TEXT NOT NULL
                    );
                    """,
                postgresUpSql: """
                    CREATE TABLE tutorial_tasks (
                        id UUID PRIMARY KEY,
                        project_id BIGINT NOT NULL,
                        title TEXT NOT NULL,
                        is_completed BOOLEAN NOT NULL,
                        created_at TIMESTAMPTZ NOT NULL
                    );
                    CREATE TABLE tutorial_audit (
                        id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                        task_id UUID NOT NULL,
                        action TEXT NOT NULL
                    );
                    """,
                sqliteDownSql: "DROP TABLE tutorial_audit; DROP TABLE tutorial_tasks;",
                postgresDownSql: "DROP TABLE tutorial_audit; DROP TABLE tutorial_tasks;"),
            MigrationStep.Universal(
                number: 2,
                upSql: "ALTER TABLE tutorial_tasks ADD COLUMN note TEXT",
                downSql: "ALTER TABLE tutorial_tasks DROP COLUMN note")
        ],
        releases:
        [
            new() { Release = "0.0.0", Number = 0 },
            new() { Release = "1.0.0", Number = 1 },
            new() { Release = "1.1.0", Number = 2 }
        ]);

    public async Task ResetSchemaAsync(IDbProvider provider, CancellationToken cancellationToken)
    {
        await using var connection = await provider.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS tutorial_audit; DROP TABLE IF EXISTS tutorial_tasks; DROP TABLE IF EXISTS nyssa_dbgateway_migrations;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
