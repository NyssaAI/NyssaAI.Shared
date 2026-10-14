using System.Data;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using Xunit;

namespace NyssaAI.Repository.Tests.Providers;

public sealed class RepositoryProviderTests
{
    [Theory]
    [InlineData("sqlite", "Data Source=:memory:")]
    [InlineData("postgresql", null)]
    public async Task OpenConnectionAsync_RealDatabase_IndependentConnectionsAndDisposal(string provider, string? connectionString)
    {
        connectionString ??= Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "Set NYSSAAI_REPOSITORY_POSTGRES to the isolated test database; PostgreSQL is not silently skipped.");
        var settings = new RepositoryConfiguration { DatabaseProvider = provider, ConnectionString = connectionString };
        var created = provider == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings);

        await using (var selected = created())
        {
            var cancellation = TestContext.Current.CancellationToken;
            var first = await selected.OpenConnectionAsync(cancellation);

            await using (var firstConnection = first)
            {
                var second = await selected.OpenConnectionAsync(cancellation);

                await using (var secondConnection = second)
                {
                    Assert.NotSame(first, second);
                    Assert.Equal(ConnectionState.Open, first.State);
                    await first.DisposeAsync();
                    Assert.Equal(ConnectionState.Open, second.State);
                    await using (var command = second.CreateCommand())
                    {
                        command.CommandText = "SELECT 1";
                        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(cancellation)));
                    }
                    await second.DisposeAsync();
                    await selected.DisposeAsync();
                    await selected.DisposeAsync();
                    var afterDisposal = await Assert.ThrowsAsync<ObjectDisposedException>(() => selected.OpenConnectionAsync(cancellation));

                }
            }
        }
    }

    [Theory]
    [InlineData("sqlite", "bad syntax sentinel-secret")]
    [InlineData("sqlite", "Data Source= ")]
    [InlineData("postgresql", "bad syntax sentinel-secret")]
    [InlineData("postgresql", "Host= ")]
    public void Create_InvalidConnection_FailsWithoutExposingSettings(string provider, string connection)
    {
        var settings = new RepositoryConfiguration { DatabaseProvider = provider, ConnectionString = connection };
        var result = Assert.Throws<ArgumentException>(() => provider == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings));

        Assert.DoesNotContain("sentinel-secret", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlite", "Data Source=:memory:")]
    [InlineData("postgresql", "Host=localhost;Password=sentinel-secret")]
    public async Task OpenConnectionAsync_PreCanceled_ReturnsCanceled(string provider, string connection)
    {
        var settings = new RepositoryConfiguration { DatabaseProvider = provider, ConnectionString = connection };
        var created = provider == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings);

        await using (var selected = created())
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var result = await Assert.ThrowsAsync<OperationCanceledException>(() => selected.OpenConnectionAsync(cancellation.Token));
                Assert.Equal(cancellation.Token, result.CancellationToken);
            }
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_SqliteReadWriteMissingFile_ThrowsSqliteExceptionWithoutCreatingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "f001-missing-file-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "sentinel-secret.db");
        try
        {
            Directory.CreateDirectory(directory);
            var created = RepositoryProviderSqlite.Prepare(new RepositoryConfiguration
            {
                DatabaseProvider = "sqlite",
                ConnectionString = $"Data Source={file};Mode=ReadWrite"
            });

            await using (var provider = created())
            {
                Assert.True(Directory.Exists(directory));
                Assert.False(File.Exists(file));

                var result = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => provider.OpenConnectionAsync(TestContext.Current.CancellationToken));

                Assert.DoesNotContain("sentinel-secret", result.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(file));
                Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_PostgresUnavailable_PreservesNpgsqlException()
    {
        var created = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql",
            ConnectionString = "Host=127.0.0.1;Port=1;Username=f001;Password=sentinel-secret;Timeout=1"
        });

        await using (var provider = created())
        {
            var result = await Assert.ThrowsAsync<NpgsqlException>(() => provider.OpenConnectionAsync(TestContext.Current.CancellationToken));

            Assert.DoesNotContain("sentinel-secret", result.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_PostgresAuthenticationRefused_PreservesSqlState()
    {
        var connectionString = Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "Set NYSSAAI_REPOSITORY_POSTGRES to the isolated test database.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Password = "sentinel-wrong-password" };
        var created = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql", ConnectionString = settings.ConnectionString
        });

        await using (var provider = created())
        {
            var result = await Assert.ThrowsAsync<PostgresException>(() => provider.OpenConnectionAsync(TestContext.Current.CancellationToken));

            Assert.Equal(PostgresErrorCodes.InvalidPassword, result.SqlState);
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_StartupTimeoutAfterCallerCancellation_PreservesTimeoutAndReleasesPoolSlot()
    {
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var created = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
                {
                    DatabaseProvider = "postgresql",
                    ConnectionString = $"Host=127.0.0.1;Port={port};Username=f001;Password=sentinel-secret;Maximum Pool Size=1;Timeout=1;SSL Mode=Disable;GSS Encryption Mode=Disable"
                });

                await using (var provider = created())
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                        {
                            var opening = provider.OpenConnectionAsync(cancellation.Token);
                            using (var accepted = await listener.AcceptTcpClientAsync(timeout.Token))
                            {
                                var startup = new byte[1024];
                                Assert.True(await accepted.GetStream().ReadAsync(startup, timeout.Token) > 0);
                                await cancellation.CancelAsync();
                                var result = await Assert.ThrowsAsync<NpgsqlException>(() => opening);
                                Assert.IsType<TimeoutException>(result.InnerException);
                                Assert.True(cancellation.IsCancellationRequested);
                                Assert.DoesNotContain("sentinel-secret", result.Message);
                                var buffer = new byte[1024];
                                while (await accepted.GetStream().ReadAsync(buffer, timeout.Token) != 0)
                                {
                                    // Drain the startup packet; EOF proves the canceled connection did not retain its socket.
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_HandshakeTimeout_PreservesDriverTimeout()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var create = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
            {
                DatabaseProvider = "postgresql",
                ConnectionString = $"Host=127.0.0.1;Port={port};Username=test;Timeout=1;SSL Mode=Disable;GSS Encryption Mode=Disable"
            });
            await using (var provider = create())
            {
                var opening = provider.OpenConnectionAsync(timeout.Token);
                using (var accepted = await listener.AcceptTcpClientAsync(timeout.Token))
                {
                    var failure = await Assert.ThrowsAsync<NpgsqlException>(() => opening);
                    Assert.IsType<TimeoutException>(failure.InnerException);
                    Assert.False(timeout.IsCancellationRequested);
                }
            }
        }
    }

    [Fact]
    public async Task AddNyssaRepository_Sqlite_DoesNotCreateFileUntilOpened()
    {
        var directory = Path.Combine(Path.GetTempPath(), "f001-no-eager-io-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "repository.db");
        try
        {
            Directory.CreateDirectory(directory);
            var settings = new RepositoryConfiguration { DatabaseProvider = "sqlite", ConnectionString = $"Data Source={file}" };
            var builder = Host.CreateApplicationBuilder();
            var services = builder.Services;
            _ = services.AddNyssaRepository(settings);
            using (var host = builder.Build())
            {
                var container = host.Services;
                var provider = container.GetRequiredService<IRepositoryProvider>();
                Assert.False(File.Exists(file));
                var opened = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken);

                await using (var connection = opened)
                {
                    Assert.True(File.Exists(file));
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
