using System.Data.Common;
using Npgsql;
using NyssaAI.Repository.Configuration;

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

    public static Func<IRepositoryProvider> Prepare(RepositoryConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
            if (string.IsNullOrWhiteSpace(parsed.Host) || parsed.MinPoolSize > parsed.MaxPoolSize || !HasValidEmbeddedPorts(parsed.Host))
                throw new ArgumentException("PostgreSQL connection settings are invalid.", nameof(settings));
            var connectionString = settings.ConnectionString;
            return () => new RepositoryProviderPostgres(connectionString);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("PostgreSQL connection settings are invalid.", nameof(settings));
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

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? pending = null;
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                _dataSource ??= new NpgsqlSlimDataSourceBuilder(_connectionString).EnableTransportSecurity().Build();
                pending = _dataSource.CreateConnection();
            }
            await pending.OpenAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var opened = pending;
            pending = null;
            return opened;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Repository opening was canceled.", exception, cancellationToken);
        }
        catch (NpgsqlException exception) when (cancellationToken.IsCancellationRequested &&
            exception.InnerException is OperationCanceledException canceled && canceled.CancellationToken == cancellationToken)
        {
            throw new OperationCanceledException("Repository opening was canceled.", exception, cancellationToken);
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
