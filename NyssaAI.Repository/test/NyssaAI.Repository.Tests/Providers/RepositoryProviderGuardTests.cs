using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using Xunit;

namespace NyssaAI.Repository.Tests.Providers;

public sealed class RepositoryProviderGuardTests
{
    [Theory]
    [InlineData("entry", typeof(ArgumentException))]
    [InlineData("name", typeof(ArgumentException))]
    [InlineData("prepare", typeof(ArgumentException))]
    [InlineData("duplicate", typeof(InvalidOperationException))]
    [InlineData("missing", typeof(InvalidOperationException))]
    [InlineData("factory", typeof(InvalidOperationException))]
    public void RegisterDatabaseProviders_InvalidInput_ReturnsGuardFailure(string scenario, Type exceptionType)
    {
        var settings = Settings("sqlite");
        var calls = 0;
        var registration = new RepositoryProviderRegistration
        {
            DatabaseProvider = "sqlite",
            Prepare = value => { calls++; return RepositoryProviderSqlite.Prepare(value); }
        };
        // Nulls deliberately exercise runtime boundaries despite non-nullable signatures.
        IReadOnlyList<RepositoryProviderRegistration> registrations = scenario switch
        {
            "registrations" => null!,
            "entry" => [null!],
            "name" => [registration with { DatabaseProvider = " " }],
            "prepare" => [registration with { Prepare = null! }],
            "duplicate" => [registration, registration with { DatabaseProvider = " SQLITE " }],
            "missing" => [],
            "factory" => [registration with { Prepare = _ => null! }],
            _ => [registration]
        };

        Assert.Throws(exceptionType, () => RepositoryProviderRegistry.RegisterDatabaseProviders(settings, registrations));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("settings", "settings")]
    [InlineData("registrations", "registrations")]
    [InlineData("both", "settings")]
    [InlineData("provider", "DatabaseProvider")]
    [InlineData("connection", "ConnectionString")]
    public void RegisterDatabaseProviders_InvalidParameters_ThrowsValidationBeforePreparation(string scenario, string parameterName)
    {
        var settings = Settings("sqlite");
        if (scenario == "provider") settings = settings with { DatabaseProvider = " " };
        if (scenario == "connection") settings = settings with { ConnectionString = " " };

        var calls = 0;
        IReadOnlyList<RepositoryProviderRegistration> registrations =
        [
            new()
            {
                DatabaseProvider = "sqlite",
                Prepare = value => { calls++; return RepositoryProviderSqlite.Prepare(value); }
            }
        ];

        var exception = Assert.ThrowsAny<ArgumentException>(() => RepositoryProviderRegistry.RegisterDatabaseProviders(
            scenario is "settings" or "both" ? null! : settings,
            scenario is "registrations" or "both" ? null! : registrations));

        Assert.Contains(parameterName, exception.Message);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public async Task OpenConnectionAsync_CanceledOrDisposed_ReturnsGuardFailureWithoutOpening(string providerName)
    {
        var prepared = providerName == "sqlite"
            ? RepositoryProviderSqlite.Prepare(Settings(providerName))
            : RepositoryProviderPostgres.Prepare(Settings(providerName));

        await using (var provider = prepared())
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => provider.OpenConnectionAsync(cancellation.Token));
                Assert.Equal(cancellation.Token, canceled.CancellationToken);

                await provider.DisposeAsync();
                var disposed = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.OpenConnectionAsync(cancellation.Token));

            }
        }
    }

    [Theory]
    [InlineData("sqlite", "Data Source= ")]
    [InlineData("postgresql", "Host= ")]
    [InlineData("postgresql", "Host=localhost;Minimum Pool Size=2;Maximum Pool Size=1")]
    [InlineData("postgresql", "Host=localhost:invalid")]
    public void Prepare_InvalidConnection_ReturnsValidation(string providerName, string connectionString)
    {
        var settings = Settings(providerName) with { ConnectionString = connectionString };
        var result = Assert.ThrowsAny<ArgumentException>(() => providerName == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings));

    }

    private static RepositoryConfiguration Settings(string providerName) => new()
    {
        DatabaseProvider = providerName,
        ConnectionString = providerName == "sqlite" ? "Data Source=:memory:" : "Host=localhost"
    };
}
