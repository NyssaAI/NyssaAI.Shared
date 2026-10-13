using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
using Xunit;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryProviderTutorials
{
    [Fact]
    public async Task Configure_SwitchDatabase_ExistingProviderKeepsSnapshotAsync()
    {
        // Arrange
        using var database = new DatabaseWorkspace();
        using var configurationHost = new ConfigurationHost();
        var store = configurationHost.Open(database.DirectoryPath);
        using var existingHost = new RepositoryHost();
        using var replacementHost = new RepositoryHost();
        var postgres = database.ConnectionString("postgresql");
        var cancellationToken = TestContext.Current.CancellationToken;
        Result existingRegistration = Result.Success();
        Result replacementRegistration = Result.Success();

        // Act
        // Plain is explicit for the first save. This record supplies a later host's database settings.
        var saved = await store.SetRepositoryConfigurationAsync(new RepositoryConfiguration
        {
            DatabaseProvider = "sqlite", ConnectionString = "Data Source=:memory:"
        }, storageProvider: ConfigurationStore.Plain, cancellationToken: cancellationToken);
        saved.ThrowIfError(); // Startup cannot read settings that failed to persist; this boundary throws.

        await existingHost.RegisterAsync(async services =>
        {
            existingRegistration = await services.AddNyssaRepositoryAsync(store, cancellationToken: cancellationToken);
            existingRegistration.ThrowIfError();
        });
        var existing = existingHost.Host.GetRequiredService<IRepositoryProvider>();

        // Saving changes affects future registrations, not an existing provider's captured settings.
        var updated = await store.SetRepositoryConfigurationAsync(new RepositoryConfiguration
        {
            DatabaseProvider = "postgresql", ConnectionString = postgres
        }, cancellationToken: cancellationToken);
        updated.ThrowIfError();

        await replacementHost.RegisterAsync(async services =>
        {
            replacementRegistration = await services.AddNyssaRepositoryAsync(store, cancellationToken: cancellationToken);
            replacementRegistration.ThrowIfError();
        });
        var replacement = replacementHost.Host.GetRequiredService<IRepositoryProvider>();

        // Both captured providers remain usable; each owns the connection returned by its Result.
        var existingOpened = await existing.OpenConnectionAsync(cancellationToken);
        await using var existingConnection = existingOpened.Unwrap();
        await using var existingQuery = existingConnection.CreateCommand();
        existingQuery.CommandText = "SELECT 1";
        var existingValue = Convert.ToInt64(await existingQuery.ExecuteScalarAsync(cancellationToken));

        var replacementOpened = await replacement.OpenConnectionAsync(cancellationToken);
        await using var replacementConnection = replacementOpened.Unwrap();
        await using var replacementQuery = replacementConnection.CreateCommand();
        replacementQuery.CommandText = "SELECT 1";
        var replacementValue = Convert.ToInt64(await replacementQuery.ExecuteScalarAsync(cancellationToken));

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.True(existingRegistration.IsSuccess, existingRegistration.ToString());
        Assert.True(updated.IsSuccess, updated.ToString());
        Assert.True(replacementRegistration.IsSuccess, replacementRegistration.ToString());
        Assert.Equal("sqlite", existing.DatabaseProvider);
        Assert.Equal("postgresql", replacement.DatabaseProvider);
        Assert.True(existingOpened.IsSuccess, existingOpened.ToString());
        Assert.True(replacementOpened.IsSuccess, replacementOpened.ToString());
        Assert.Equal(1L, existingValue);
        Assert.Equal(1L, replacementValue);
    }

    [Fact]
    public async Task Configure_CompiledCustomProvider_UsesPublicRegistrationSeamAsync()
    {
        // Arrange
        using var host = new RepositoryHost();
        var cancellationToken = TestContext.Current.CancellationToken;
        Result registration = Result.Success();

        // Act
        // A provider contributes connections through the public seam; repository workflows stay Nyssa's.
        var custom = new RepositoryProviderRegistration
        {
            DatabaseProvider = "application-sqlite",
            Prepare = settings => Result<Func<IRepositoryProvider>>.Success(() => new ApplicationProvider(settings))
        };
        host.Register(services =>
        {
            registration = services.AddNyssaRepository(new RepositoryConfiguration
            {
                DatabaseProvider = "APPLICATION-SQLITE", ConnectionString = "Data Source=:memory:"
            }, [custom]);
            registration.ThrowIfError(); // Resolving a provider requires successful startup.
        });
        var provider = host.Host.GetRequiredService<IRepositoryProvider>();

        var opened = await provider.OpenConnectionAsync(cancellationToken);
        await using var connection = opened.Unwrap(); // A query needs a live connection; failure throws here.
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT 1";
        var value = Convert.ToInt64(await query.ExecuteScalarAsync(cancellationToken));

        // Assert
        Assert.True(registration.IsSuccess, registration.ToString());
        Assert.Equal("application-sqlite", provider.DatabaseProvider);
        Assert.True(opened.IsSuccess, opened.ToString());
        Assert.Equal(1L, value);
    }
}
