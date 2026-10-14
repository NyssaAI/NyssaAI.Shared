using System.Data.Common;
using Microsoft.Data.Sqlite;
using NyssaAI.Repository.Configuration;

namespace NyssaAI.Repository.Providers;

/// <summary>Opens independently owned SQLite connections without a process-global connection pool.</summary>
internal sealed class RepositoryProviderSqlite : IRepositoryProvider
{
    private readonly string _connectionString;
    private int _disposed;

    private RepositoryProviderSqlite(string connectionString) => _connectionString = connectionString;

    public string DatabaseProvider => "sqlite";

    public static Func<IRepositoryProvider> Prepare(RepositoryConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        try
        {
            var parsed = new SqliteConnectionStringBuilder(settings.ConnectionString);
            ArgumentException.ThrowIfNullOrWhiteSpace(parsed.DataSource);
            // SQLite's built-in pool is process-global; disabling it keeps file handles in the caller's connection lifetime.
            parsed.Pooling = false;
            var connectionString = parsed.ConnectionString;
            return () => new RepositoryProviderSqlite(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw new ArgumentException("SQLite connection settings are invalid.", nameof(settings));
        }
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        SqliteConnection? pending = null;
        try
        {
            pending = new SqliteConnection(_connectionString);
            await pending.OpenAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var opened = pending;
            pending = null;
            return opened;
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
