using NyssaAI.Repository.Providers;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

/// <summary>Provides isolated local files and database schema setup, without test assertions.</summary>
public sealed class DatabaseWorkspace : IDisposable
{
    private readonly string _directory = Directory
        .CreateTempSubdirectory("nyssa-repository-tutorial-")
        .FullName;
    public string DirectoryPath => _directory;

    public string ConnectionString(string provider) =>
        provider == "sqlite"
            ? $"Data Source={Path.Join(_directory, "tutorial.db")};Pooling=False"
            : Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES")
                ?? throw new InvalidOperationException(
                    "Set NYSSAAI_REPOSITORY_POSTGRES to an isolated test database."
                );

    public Task PrepareTasksAsync(
        IRepositoryProvider provider,
        CancellationToken cancellationToken
    ) =>
        ExecuteSetupAsync(
            provider,
            "DROP TABLE IF EXISTS f002_outbox; DROP TABLE IF EXISTS f002_tasks; "
                + "CREATE TABLE f002_tasks (id BIGINT PRIMARY KEY, title TEXT NOT NULL, note TEXT NULL); "
                + "CREATE TABLE f002_outbox (task_id BIGINT PRIMARY KEY, title TEXT NOT NULL)",
            cancellationToken
        );

    public Task ResetMigrationsAsync(
        IRepositoryProvider provider,
        CancellationToken cancellationToken
    ) =>
        ExecuteSetupAsync(
            provider,
            "DROP TABLE IF EXISTS migration_tutorial_tasks; DROP TABLE IF EXISTS migration_tutorial_outbox; DROP TABLE IF EXISTS nyssa_repository_migrations",
            cancellationToken
        );

    private static async Task ExecuteSetupAsync(
        IRepositoryProvider provider,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using (var connection = await provider.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
