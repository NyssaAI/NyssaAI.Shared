using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.Tests.Providers;

public sealed class RepositoryProviderGuardTests
{
    [Theory]
    [InlineData("entry", ErrorCategory.Validation)]
    [InlineData("name", ErrorCategory.Validation)]
    [InlineData("prepare", ErrorCategory.Validation)]
    [InlineData("duplicate", ErrorCategory.Conflict)]
    [InlineData("missing", ErrorCategory.Validation)]
    [InlineData("result", ErrorCategory.Unexpected)]
    [InlineData("factory", ErrorCategory.Unexpected)]
    public void RegisterDatabaseProviders_InvalidInput_ReturnsGuardFailure(string scenario, ErrorCategory category)
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
            "result" => [registration with { Prepare = _ => null! }],
            "factory" => [registration with { Prepare = _ => Result<Func<IRepositoryProvider>>.Success(null!) }],
            _ => [registration]
        };

        var result = RepositoryProviderRegistry.RegisterDatabaseProviders(scenario == "settings" ? null! : settings, registrations);

        Assert.Equal(category, Assert.Single(result.Errors).Category);
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

        var exception = Assert.Throws<ResultException>(() => RepositoryProviderRegistry.RegisterDatabaseProviders(
            scenario is "settings" or "both" ? null! : settings,
            scenario is "registrations" or "both" ? null! : registrations));

        Assert.Equal(ErrorCategory.Validation, Assert.Single(exception.Errors).Category);
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
        Assert.True(prepared.IsSuccess);
        await using var provider = prepared.Value();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = await provider.OpenConnectionAsync(cancellation.Token);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(canceled.Errors).Category);

        await provider.DisposeAsync();
        var disposed = await provider.OpenConnectionAsync(cancellation.Token);
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(disposed.Errors).Category);
    }

    [Theory]
    [InlineData("sqlite", "Data Source= ")]
    [InlineData("postgresql", "Host= ")]
    [InlineData("postgresql", "Host=localhost;Minimum Pool Size=2;Maximum Pool Size=1")]
    [InlineData("postgresql", "Host=localhost:invalid")]
    public void Prepare_InvalidConnection_ReturnsValidation(string providerName, string connectionString)
    {
        var settings = Settings(providerName) with { ConnectionString = connectionString };
        var result = providerName == "sqlite" ? RepositoryProviderSqlite.Prepare(settings) : RepositoryProviderPostgres.Prepare(settings);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    private static RepositoryConfiguration Settings(string providerName) => new()
    {
        DatabaseProvider = providerName,
        ConnectionString = providerName == "sqlite" ? "Data Source=:memory:" : "Host=localhost"
    };
}
