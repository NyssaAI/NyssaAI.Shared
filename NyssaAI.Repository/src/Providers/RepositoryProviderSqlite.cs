using System.Data.Common;
using Microsoft.Data.Sqlite;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;

namespace NyssaAI.Repository.Providers;

/// <summary>Opens independently owned SQLite connections without a process-global connection pool.</summary>
internal sealed class RepositoryProviderSqlite : IRepositoryProvider
{
    private readonly string _connectionString;
    private int _disposed;

    private RepositoryProviderSqlite(string connectionString) => _connectionString = connectionString;

    public string DatabaseProvider => "sqlite";

    public static Result<Func<IRepositoryProvider>> Prepare(RepositoryConfiguration settings)
    {
        try
        {
            var parsed = new SqliteConnectionStringBuilder(settings.ConnectionString);
            var dataSource = Guard.Against.NullOrWhiteSpace(parsed.DataSource, nameof(parsed.DataSource));
            if (dataSource.IsFailure) return Result<Func<IRepositoryProvider>>.Failure(dataSource.Errors);
            // SQLite's built-in pool is process-global; disabling it keeps file handles in the caller's connection lifetime.
            parsed.Pooling = false;
            var connectionString = parsed.ConnectionString;
            return Result<Func<IRepositoryProvider>>.Success(() => new RepositoryProviderSqlite(connectionString));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return Result<Func<IRepositoryProvider>>.Failure(Error.FromMessage("SQLite connection settings are invalid.", ErrorCategory.Validation));
        }
    }

    public async Task<Result<DbConnection>> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var available = Guard.Against.True(Volatile.Read(ref _disposed) != 0, "The database provider is disposed.", category: ErrorCategory.Conflict);
        if (available.IsFailure) return Result<DbConnection>.Failure(available.Errors);
        var active = Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled);
        if (active.IsFailure) return Result<DbConnection>.Failure(active.Errors);
        SqliteConnection? pending = null;
        try
        {
            pending = new SqliteConnection(_connectionString);
            await pending.OpenAsync(cancellationToken).ConfigureAwait(false);
            active = Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled);
            if (active.IsFailure) return Result<DbConnection>.Failure(active.Errors);
            var opened = pending;
            pending = null;
            return Result<DbConnection>.Success(opened);
        }
        catch (OperationCanceledException)
        {
            return Result<DbConnection>.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result<DbConnection>.Failure(Error.FromMessage("The SQLite connection could not be opened.", ErrorCategory.Unavailable));
        }
        finally
        {
            if (pending is not null)
                await pending.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

}
