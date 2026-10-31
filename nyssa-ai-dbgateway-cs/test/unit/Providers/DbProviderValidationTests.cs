using Microsoft.Extensions.DependencyInjection;
using NyssaAI.DbGateway.Configuration;
using Xunit;

namespace NyssaAI.DbGateway.Tests.Providers;

public sealed class DbProviderValidationTests
{
    [Fact]
    public void RegisterDatabaseProviders_SqliteAndPostgresSettings_ValidatesConnectionStringsWithoutIO()
    {
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(new DbGatewayConfiguration
        {
            DatabaseProvider = "  SQLITE  ",
            ConnectionString = "Data Source=local.db"
        });

        var postgresServices = new ServiceCollection();
        postgresServices.AddNyssaDbGateway(new DbGatewayConfiguration
        {
            DatabaseProvider = "PostgreSQL",
            ConnectionString = "Host=localhost;Port=5432;Database=test;Username=user;Password=pass"
        });

        Assert.Contains(services, d => d.ServiceType == typeof(IDbGateway));
        Assert.Contains(postgresServices, d => d.ServiceType == typeof(IDbGateway));
    }

    [Theory]
    [InlineData("sqlite", "")]
    [InlineData("sqlite", "Mode=Memory")]
    [InlineData("postgresql", "Database=test")]
    [InlineData("postgresql", "Host=localhost:0;Database=test")]
    [InlineData("postgresql", "Host=localhost;MinPoolSize=10;MaxPoolSize=5")]
    public void AddNyssaDbGateway_InvalidConnectionString_ThrowsArgumentException(string provider, string connectionString)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddNyssaDbGateway(new DbGatewayConfiguration
            {
                DatabaseProvider = provider,
                ConnectionString = connectionString
            }));
    }

    [Fact]
    public void AddNyssaDbGateway_DuplicateRegistrationOrUnknownProvider_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddNyssaDbGateway(new DbGatewayConfiguration
        {
            DatabaseProvider = "sqlite",
            ConnectionString = "Data Source=local.db"
        });

        Assert.Throws<InvalidOperationException>(() =>
            services.AddNyssaDbGateway(new DbGatewayConfiguration
            {
                DatabaseProvider = "sqlite",
                ConnectionString = "Data Source=other.db"
            }));

        var unknownServices = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            unknownServices.AddNyssaDbGateway(new DbGatewayConfiguration
            {
                DatabaseProvider = "oracle",
                ConnectionString = "Data Source=ora"
            }));
    }

    [Fact]
    public void ToString_DbGatewayConfiguration_DoesNotLeakConnectionString()
    {
        var config = new DbGatewayConfiguration
        {
            DatabaseProvider = "postgresql",
            ConnectionString = "Host=db.internal;Password=super-secret-password"
        };

        Assert.Equal(nameof(DbGatewayConfiguration), config.ToString());
        Assert.DoesNotContain("super-secret-password", config.ToString(), StringComparison.Ordinal);
    }
}
