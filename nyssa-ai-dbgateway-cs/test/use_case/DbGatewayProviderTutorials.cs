using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Commands;
using NyssaAI.DbGateway.Configuration;
using NyssaAI.DbGateway.Providers;
using Xunit;

namespace NyssaAI.DbGateway.UseCaseTests;

public sealed class DbGatewayProviderTutorials
{
    [Fact]
    public async Task AddNyssaDbGateway_CustomProvider_ExecutesTypedCommand()
    {
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(
            new DbGatewayConfiguration
            {
                DatabaseProvider = "custom-sqlite",
                ConnectionString = "Data Source=:memory:"
            },
            providers:
            [
                new DbProviderRegistration
                {
                    DatabaseProvider = "custom-sqlite",
                    Prepare = settings => () => new CustomSqliteProvider(settings.ConnectionString)
                }
            ]);

        await using var container = services.BuildServiceProvider();
        var gateway = container.GetRequiredService<IDbGateway>();
        Assert.Equal("custom-sqlite", gateway.DatabaseProvider);

        var result = await gateway.ExecuteAsync(new PingCommand { Echo = 99 }, TestContext.Current.CancellationToken);
        Assert.Equal(99, result);
    }

    private sealed class CustomSqliteProvider : IDbProvider
    {
        private readonly string _connectionString;
        public CustomSqliteProvider(string connectionString) => _connectionString = connectionString;
        public string DatabaseProvider => "custom-sqlite";
        public DbDialect Dialect => DbDialect.Sqlite;

        public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record PingCommand : IDbCommand<int>
    {
        public required int Echo { get; init; }
        public string GetSql(DbDialect dialect) => "SELECT @echo";
        public void BindParameters(IParameterBinder parameters, DbDialect dialect) => parameters.Add("@echo", Echo);
        public async ValueTask<int> MapResultAsync(IDataRecordReader reader, CancellationToken cancellationToken = default)
        {
            await reader.ReadAsync(cancellationToken);
            return reader.GetInt32(0);
        }
    }
}
