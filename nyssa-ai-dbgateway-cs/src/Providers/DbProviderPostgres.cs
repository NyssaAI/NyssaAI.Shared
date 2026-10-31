using System.Data.Common;
using Npgsql;
using NyssaAI.DbGateway.Configuration;

namespace NyssaAI.DbGateway.Providers;

/// <summary>Owns one lazily initialized Npgsql data source and opens independent pooled connections.</summary>
internal sealed class DbProviderPostgres : IDbProvider
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private NpgsqlDataSource? _dataSource;
    private bool _disposed;

    private DbProviderPostgres(string connectionString) => _connectionString = connectionString;

    public string DatabaseProvider => "postgresql";

    public DbDialect Dialect => DbDialect.PostgreSql;

    public static Func<IDbProvider> Prepare(DbGatewayConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
            if (string.IsNullOrWhiteSpace(parsed.Host) || parsed.MinPoolSize > parsed.MaxPoolSize || !HasValidEmbeddedPorts(parsed.Host))
                throw new ArgumentException("PostgreSQL connection settings are invalid.", nameof(settings));
            var connectionString = settings.ConnectionString;
            return () => new DbProviderPostgres(connectionString);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("PostgreSQL connection settings are invalid.", nameof(settings));
        }
    }

    private static bool HasValidEmbeddedPorts(string host)
    {
        if (!host.Contains(','))
            return Path.IsPathRooted(host) || host[0] == '@' || HasValidEmbeddedPort(host.AsSpan());

        foreach (var entry in host.Split(','))
        {
            if (!HasValidEmbeddedPort(entry.AsSpan().Trim()))
                return false;
        }
        return true;
    }

    private static bool HasValidEmbeddedPort(ReadOnlySpan<char> host)
    {
        var separator = host.LastIndexOf(':');
        if (separator < 0)
            return true;

        var previousColon = host[..separator].LastIndexOf(':');
        var closingBracket = host.LastIndexOf(']');
        var hasPort = previousColon < 0 || (separator > closingBracket && previousColon < closingBracket);
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
            throw new OperationCanceledException("Database connection opening was canceled.", exception, cancellationToken);
        }
        catch (NpgsqlException exception) when (cancellationToken.IsCancellationRequested &&
            exception.InnerException is OperationCanceledException canceled && canceled.CancellationToken == cancellationToken)
        {
            throw new OperationCanceledException("Database connection opening was canceled.", exception, cancellationToken);
        }
        finally
        {
            if (pending is not null)
                await pending.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        NpgsqlDataSource? dataSource;
        lock (_gate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
            dataSource = _dataSource;
            _dataSource = null;
        }
        return dataSource?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
