using System.Data;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
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
        Assert.True(created.IsSuccess, created.ToString());
        await using var selected = created.Value();
        var cancellation = TestContext.Current.CancellationToken;
        var first = await selected.OpenConnectionAsync(cancellation);
        Assert.True(first.IsSuccess, first.ToString());
        await using var firstConnection = first.Value;
        var second = await selected.OpenConnectionAsync(cancellation);
        Assert.True(second.IsSuccess, second.ToString());
        await using var secondConnection = second.Value;
        Assert.NotSame(first.Value, second.Value);
        Assert.Equal(ConnectionState.Open, first.Value.State);
        await first.Value.DisposeAsync();
        Assert.Equal(ConnectionState.Open, second.Value.State);
        await using (var command = second.Value.CreateCommand())
        {
            command.CommandText = "SELECT 1";
            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(cancellation)));
        }
        await second.Value.DisposeAsync();
        await selected.DisposeAsync();
        await selected.DisposeAsync();
        var afterDisposal = await selected.OpenConnectionAsync(cancellation);
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(afterDisposal.Errors).Category);
    }

    [Theory]
    [InlineData("sqlite", "bad syntax sentinel-secret")]
    [InlineData("sqlite", "Data Source= ")]
    [InlineData("postgresql", "bad syntax sentinel-secret")]
    [InlineData("postgresql", "Host= ")]
    public void Create_InvalidConnection_FailsWithoutExposingSettings(string provider, string connection)
    {
        var settings = new RepositoryConfiguration { DatabaseProvider = provider, ConnectionString = connection };
        var result = provider == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("sqlite", "Data Source=:memory:")]
    [InlineData("postgresql", "Host=localhost;Password=sentinel-secret")]
    public async Task OpenConnectionAsync_PreCanceled_ReturnsCanceled(string provider, string connection)
    {
        var settings = new RepositoryConfiguration { DatabaseProvider = provider, ConnectionString = connection };
        var created = provider == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings);
        Assert.True(created.IsSuccess);
        await using var selected = created.Value();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await selected.OpenConnectionAsync(cancellation.Token);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public async Task OpenConnectionAsync_SqliteReadWriteMissingFile_ReturnsSanitizedUnavailableWithoutCreatingFile()
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
            Assert.True(created.IsSuccess);
            await using var provider = created.Value();
            Assert.True(Directory.Exists(directory));
            Assert.False(File.Exists(file));

            var result = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken);
            if (result.IsSuccess)
                await result.Value.DisposeAsync();

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
            Assert.All(result.Errors, error =>
            {
                Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
                Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
                Assert.DoesNotContain(file, error.UserMessage, StringComparison.Ordinal);
                Assert.DoesNotContain(file, error.TechnicalMessage, StringComparison.Ordinal);
            });
            Assert.False(File.Exists(file));
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_PostgresUnavailable_SanitizesFailure()
    {
        var created = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql",
            ConnectionString = "Host=127.0.0.1;Port=1;Username=f001;Password=sentinel-secret;Timeout=1"
        });
        Assert.True(created.IsSuccess);
        await using var provider = created.Value();
        var result = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCategory.Unavailable, Assert.Single(result.Errors).Category);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task OpenConnectionAsync_PostgresAuthenticationRefused_SanitizesFailure()
    {
        var connectionString = Environment.GetEnvironmentVariable("NYSSAAI_REPOSITORY_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "Set NYSSAAI_REPOSITORY_POSTGRES to the isolated test database.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { Password = "sentinel-wrong-password" };
        var created = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql", ConnectionString = settings.ConnectionString
        });
        Assert.True(created.IsSuccess);
        await using var provider = created.Value();
        var result = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCategory.Forbidden, Assert.Single(result.Errors).Category);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("sentinel-wrong-password", error.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-wrong-password", error.TechnicalMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task OpenConnectionAsync_CanceledDuringHandshake_ClosesSocketAndReleasesPoolSlot()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var created = RepositoryProviderPostgres.Prepare(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql",
            ConnectionString = $"Host=127.0.0.1;Port={port};Username=f001;Password=sentinel-secret;Maximum Pool Size=1;Timeout=1"
        });
        Assert.True(created.IsSuccess);
        await using var provider = created.Value();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var opening = provider.OpenConnectionAsync(cancellation.Token);
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            await cancellation.CancelAsync();
            var result = await opening;
            Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
            Assert.All(result.Errors, error =>
            {
                Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
                Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
            });
            var buffer = new byte[1024];
            while (await accepted.GetStream().ReadAsync(buffer, timeout.Token) != 0)
            {
                // Drain the startup packet; EOF proves the canceled connection did not retain its socket.
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
            Assert.True(services.AddNyssaRepository(settings).IsSuccess);
            using var host = builder.Build();
            var container = host.Services;
            var provider = container.GetRequiredService<IRepositoryProvider>();
            Assert.False(File.Exists(file));
            var opened = await provider.OpenConnectionAsync(TestContext.Current.CancellationToken);
            Assert.True(opened.IsSuccess);
            await using var connection = opened.Value;
            Assert.True(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
