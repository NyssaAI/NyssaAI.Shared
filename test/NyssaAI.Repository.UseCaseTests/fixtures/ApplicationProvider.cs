using System.Data.Common;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

public sealed class ApplicationProvider : IRepositoryProvider
{
    private readonly string _connectionString;
    private bool _disposed;

    public ApplicationProvider(RepositoryConfiguration settings) => _connectionString = settings.ConnectionString;

    public string DatabaseProvider => "application-sqlite";

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (OperationCanceledException)
        {
            await connection.DisposeAsync();
            throw new OperationCanceledException("Opening canceled.", cancellationToken);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
