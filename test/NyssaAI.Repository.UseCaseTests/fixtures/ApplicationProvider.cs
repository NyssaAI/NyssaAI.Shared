using System.Data.Common;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

public sealed class ApplicationProvider : IRepositoryProvider
{
    private readonly string _connectionString;
    private bool _disposed;

    public ApplicationProvider(RepositoryConfiguration settings) => _connectionString = settings.ConnectionString;

    public string DatabaseProvider => "application-sqlite";

    public async Task<Result<DbConnection>> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var guardResult = Guard.Against.True(_disposed, "Provider disposed.", category: ErrorCategory.Conflict);
        if (guardResult.IsFailure)
            return Result<DbConnection>.Failure(guardResult.Errors);

        var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return Result<DbConnection>.Success(connection);
        }
        catch (OperationCanceledException)
        {
            await connection.DisposeAsync();
            return Result<DbConnection>.Failure(Error.FromMessage("Opening canceled.", ErrorCategory.Canceled));
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            await connection.DisposeAsync();
            return Result<DbConnection>.Failure(Error.FromMessage("Database could not be opened.", ErrorCategory.Unavailable));
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
