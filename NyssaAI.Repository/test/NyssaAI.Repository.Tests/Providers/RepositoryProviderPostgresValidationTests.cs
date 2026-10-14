using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using Xunit;

namespace NyssaAI.Repository.Tests.Providers;

public sealed class RepositoryProviderPostgresValidationTests
{
    public static TheoryData<string> InvalidHosts => new()
    {
        "localhost:abc",
        "localhost:2147483648",
        "localhost:999999999999999999999999",
        "localhost:",
        "localhost:0",
        "localhost:-1",
        "[::1]:abc",
        "[::1]:2147483648",
        "localhost:5432, other:abc",
        "localhost:5432, other:2147483648",
        "localhost:abc, other:5432",
        "localhost:2147483648, other:5432",
        "::1, [2001:db8::1]:abc",
        "::1, [2001:db8::1]:2147483648",
        // Npgsql splits ports on every multi-host entry, including socket paths.
        "/tmp/postgres:abc, localhost:5432",
        "@postgres:2147483648, localhost:5432"
    };

    public static TheoryData<string> ValidHosts => new()
    {
        "database.invalid",
        "127.0.0.1:5432",
        "database.invalid:+5432",
        "database.invalid: 5432",
        // The pinned driver's Port setter accepts every positive Int32.
        "database.invalid:65536",
        "database.invalid:2147483647",
        "::1",
        "2001:db8::abc",
        "[::1]",
        "[2001:db8::1]:5432",
        "/tmp/nyssa-postgres",
        "/tmp/nyssa-postgres:abc",
        "@nyssa-postgres:2147483648",
        "database.invalid:5432, other.invalid:5433",
        "database.invalid, other.invalid:5433",
        "  database.invalid:5432 , [::1]:5433  ",
        "::1, 2001:db8::abc",
        "/tmp/nyssa-postgres, @nyssa-postgres",
        "/tmp/nyssa-postgres:5432, database.invalid:5433"
    };

    [Theory]
    [MemberData(nameof(InvalidHosts))]
    public void Prepare_InvalidEmbeddedPort_ReturnsSanitizedValidation(string host)
    {
        var result = Assert.Throws<ArgumentException>(() => RepositoryProviderPostgres.Prepare(Settings(host)));

        Assert.DoesNotContain("sentinel", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidHosts))]
    public void AddNyssaRepository_InvalidEmbeddedPort_ThrowsValidationWithoutRegistration(string host)
    {
        var services = new ServiceCollection();

        var result = Assert.Throws<ArgumentException>(() => services.AddNyssaRepository(Settings(host)));

        Assert.DoesNotContain("sentinel", result.ToString(), StringComparison.Ordinal);
        Assert.Empty(services);
    }

    [Theory]
    [MemberData(nameof(ValidHosts))]
    public async Task Prepare_DriverAcceptedHostShape_SucceedsThroughPreparationAndDi(string host)
    {
        var settings = Settings(host);
        // Construction verifies the pinned driver's parsing without opening a connection.
        await using (var driver = new NpgsqlSlimDataSourceBuilder(settings.ConnectionString)
            .EnableTransportSecurity().Build())
        {

            var created = RepositoryProviderPostgres.Prepare(settings);

            await using (var direct = created())
            {
                Assert.Equal("postgresql", direct.DatabaseProvider);

                var services = new ServiceCollection();
                var registered = services.AddNyssaRepository(settings);

                await using (var container = services.BuildServiceProvider())
                {
                    Assert.Equal("postgresql", container.GetRequiredService<IRepositoryProvider>().DatabaseProvider);
                }
            }
        }
    }

    private static RepositoryConfiguration Settings(string host) => new()
    {
        DatabaseProvider = "postgresql",
        ConnectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Username = "f001",
            Password = "sentinel-secret"
        }.ConnectionString
    };
}
