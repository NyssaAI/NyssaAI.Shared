using NyssaAI.Repository.Providers;
using NyssaAI.Results;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

/// <summary>Provides isolated local files and database schema setup, without test assertions.</summary>
public sealed class DatabaseWorkspace : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("nyssa-repository-tutorial-").FullName;

    public string DirectoryPath => _directory;

    public string ConnectionString(string provider) => provider == "sqlite"
        ? $"Data Source={Path.Join(_directory, "tutorial.db")};Pooling=False"
        : Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES")
            ?? throw new InvalidOperationException("Set NYSSAAI_REPOSITORY_POSTGRES to an isolated test database.");

    public async Task ExecuteSetupAsync(IRepositoryProvider provider, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await provider.OpenConnectionAsync(cancellationToken).UnwrapAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
