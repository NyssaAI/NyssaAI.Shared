using System.Data.Common;
using Npgsql;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;

namespace NyssaAI.Repository.Providers;

/// <summary>Owns one lazily initialized Npgsql data source and opens independent pooled connections.</summary>
internal sealed class RepositoryProviderPostgres : IRepositoryProvider
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private NpgsqlDataSource? _dataSource;
    private bool _disposed;

    private RepositoryProviderPostgres(string connectionString) => _connectionString = connectionString;

    public string DatabaseProvider => "postgresql";

    public static Result<Func<IRepositoryProvider>> Prepare(RepositoryConfiguration settings)
    {
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
            var host = Guard.Against.NullOrWhiteSpace(parsed.Host, nameof(parsed.Host));
            if (host.IsFailure) return Result<Func<IRepositoryProvider>>.Failure(host.Errors);
            var pool = Guard.Against.GreaterThan(parsed.MinPoolSize, parsed.MaxPoolSize, nameof(parsed.MinPoolSize));
            if (pool.IsFailure) return Result<Func<IRepositoryProvider>>.Failure(pool.Errors);
            var ports = Guard.Against.False(HasValidEmbeddedPorts(host.Value), "PostgreSQL connection settings are invalid.");
            if (ports.IsFailure) return Result<Func<IRepositoryProvider>>.Failure(ports.Errors);
            var connectionString = settings.ConnectionString;
            return Result<Func<IRepositoryProvider>>.Success(() => new RepositoryProviderPostgres(connectionString));
        }
        catch (ArgumentException)
        {
            return Result<Func<IRepositoryProvider>>.Failure(Error.FromMessage("PostgreSQL connection settings are invalid.", ErrorCategory.Validation));
        }
    }

    /// <summary>Validates embedded ports without allocating a data source or performing I/O.</summary>
    private static bool HasValidEmbeddedPorts(string host)
    {
        // Match Npgsql 10.0.3 PostProcessAndValidate and NpgsqlMultiHostDataSource:
        // only a single host bypasses port splitting for rooted or abstract Unix sockets.
        if (!host.Contains(','))
            return Path.IsPathRooted(host) || host[0] == '@' || HasValidEmbeddedPort(host.AsSpan());

        foreach (var entry in host.Split(','))
        {
            if (!HasValidEmbeddedPort(entry.AsSpan().Trim()))
                return false;
        }
        return true;
    }

    /// <summary>Matches the pinned driver's IPv6-aware port split and positive Int32 port constraint.</summary>
    private static bool HasValidEmbeddedPort(ReadOnlySpan<char> host)
    {
        // https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/NpgsqlConnectionStringBuilder.cs
        var separator = host.LastIndexOf(':');
        if (separator < 0)
            return true;

        var previousColon = host[..separator].LastIndexOf(':');
        var closingBracket = host.LastIndexOf(']');
        var hasPort = previousColon < 0 || (separator > closingBracket && previousColon < closingBracket);
        // Default TryParse uses the same integer styles and current culture as Npgsql's int.Parse.
        return !hasPort || (int.TryParse(host[(separator + 1)..], out var port) && port > 0);
    }

    public async Task<Result<DbConnection>> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? pending = null;
        try
        {
            lock (_gate)
            {
                var available = Guard.Against.True(_disposed, "The database provider is disposed.", category: ErrorCategory.Conflict);
                if (available.IsFailure) return Result<DbConnection>.Failure(available.Errors);
                var active = Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled);
                if (active.IsFailure) return Result<DbConnection>.Failure(active.Errors);
                try
                {
                    _dataSource ??= new NpgsqlSlimDataSourceBuilder(_connectionString).EnableTransportSecurity().Build();
                }
                catch (FormatException)
                {
                    return Result<DbConnection>.Failure(Error.FromMessage("PostgreSQL connection settings are invalid.", ErrorCategory.Validation));
                }
                catch (OverflowException)
                {
                    return Result<DbConnection>.Failure(Error.FromMessage("PostgreSQL connection settings are invalid.", ErrorCategory.Validation));
                }
                pending = _dataSource.CreateConnection();
            }
            await pending.OpenAsync(cancellationToken).ConfigureAwait(false);
            var stillActive = Guard.Against.True(cancellationToken.IsCancellationRequested, Error.CanceledUserMessage, category: ErrorCategory.Canceled);
            if (stillActive.IsFailure) return Result<DbConnection>.Failure(stillActive.Errors);
            var opened = pending;
            pending = null;
            return Result<DbConnection>.Success(opened);
        }
        catch (OperationCanceledException)
        {
            return Result<DbConnection>.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (NpgsqlException) when (cancellationToken.IsCancellationRequested)
        {
            // Npgsql can wrap a canceled handshake in its connection exception.
            return Result<DbConnection>.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled));
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification)
        {
            return Result<DbConnection>.Failure(Error.FromMessage("PostgreSQL authentication was refused.", ErrorCategory.Forbidden));
        }
        catch (ArgumentException)
        {
            return Result<DbConnection>.Failure(Error.FromMessage("PostgreSQL connection settings are invalid.", ErrorCategory.Validation));
        }
        catch (ObjectDisposedException)
        {
            return Result<DbConnection>.Failure(Error.FromMessage("The database provider is disposed.", ErrorCategory.Conflict));
        }
        catch (Exception exception) when (exception is NpgsqlException or IOException or TimeoutException or NotSupportedException)
        {
            return Result<DbConnection>.Failure(Error.FromMessage("The PostgreSQL connection could not be opened.", ErrorCategory.Unavailable));
        }
        finally
        {
            if (pending is not null)
                await pending.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlDataSource? dataSource;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            dataSource = _dataSource;
            _dataSource = null;
        }
        if (dataSource is not null)
            await dataSource.DisposeAsync().ConfigureAwait(false);
    }

}
