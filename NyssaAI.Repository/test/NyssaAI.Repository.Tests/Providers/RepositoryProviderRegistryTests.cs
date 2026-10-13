using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.Tests.Providers;

public sealed class RepositoryProviderRegistryTests
{
    [Theory]
    [InlineData("custom")]
    [InlineData(" CUSTOM ")]
    public async Task RegisterDatabaseProviders_RegisteredProvider_SelectsOnlyMatchingFactory(string name)
    {
        var prepared = 0;
        var created = 0;
        var registrations = new[]
        {
            Registration("custom", () => { prepared++; return () => { created++; return new ProviderRecording("custom"); }; }),
            Registration("unused", () => { Assert.Fail("Unselected provider must not be prepared."); return () => new ProviderRecording("unused"); })
        };
        var result = RepositoryProviderRegistry.RegisterDatabaseProviders(Settings(name), registrations);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, created);
        await using var provider = result.Value();
        Assert.Equal("custom", provider.DatabaseProvider);
        Assert.Equal(1, prepared);
        Assert.Equal(1, created);
    }

    [Theory]
    [InlineData("custom", " CUSTOM ")]
    [InlineData("unknown", "custom")]
    [InlineData("custom", " ")]
    public void RegisterDatabaseProviders_InvalidRegistry_FailsBeforePreparation(string selection, string secondName)
    {
        var calls = 0;
        Func<Func<IRepositoryProvider>> factory = () => { calls++; return () => new ProviderRecording("custom"); };
        var result = RepositoryProviderRegistry.RegisterDatabaseProviders(Settings(selection), [Registration("custom", factory), Registration(secondName, factory)]);
        Assert.True(result.IsFailure);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AddNyssaRepository_MultipleContainers_OwnDistinctInstances()
    {
        var instances = new List<ProviderRecording>();
        var services = new ServiceCollection();
        var added = services.AddNyssaRepository(Settings("custom"), [Registration("custom", () => () =>
        {
            var provider = new ProviderRecording("custom");
            instances.Add(provider);
            return provider;
        })]);
        Assert.True(added.IsSuccess);
        Assert.Empty(instances);
        await using (var unusedContainer = services.BuildServiceProvider()) { }
        Assert.Empty(instances);
        await using (var firstContainer = services.BuildServiceProvider())
        await using (var secondContainer = services.BuildServiceProvider())
        {
            var first = firstContainer.GetRequiredService<IRepositoryProvider>();
            var second = secondContainer.GetRequiredService<IRepositoryProvider>();
            Assert.Same(first, firstContainer.GetRequiredService<IRepositoryProvider>());
            Assert.NotSame(first, second);
            Assert.All(instances, instance => Assert.Equal(0, instance.Disposals));
        }
        Assert.Equal(2, instances.Count);
        Assert.All(instances, instance => Assert.Equal(1, instance.Disposals));
    }

    [Fact]
    public void AddNyssaRepository_ConflictingRegistration_DoesNotMutateServices()
    {
        var services = new ServiceCollection();
        var registrations = new[] { Registration("custom", () => () => new ProviderRecording("custom")) };
        Assert.True(services.AddNyssaRepository(Settings("custom"), registrations).IsSuccess);
        var count = services.Count;
        var repeated = Assert.Throws<ResultException>(() => services.AddNyssaRepository(Settings("custom"), registrations));
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(repeated.Errors).Category);
        Assert.Equal(count, services.Count);
    }

    [Fact]
    public void RegisterDatabaseProviders_PreparationFailure_PreservesCategoryWithoutSecrets()
    {
        var registration = new RepositoryProviderRegistration
        {
            DatabaseProvider = "custom",
            Prepare = _ => Result<Func<IRepositoryProvider>>.Failure(Error.FromMessage("sentinel-secret", ErrorCategory.Validation))
        };
        var result = RepositoryProviderRegistry.RegisterDatabaseProviders(Settings("custom"), [registration]);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
        });
    }

    private static RepositoryConfiguration Settings(string provider) => new() { DatabaseProvider = provider, ConnectionString = "sentinel-secret" };

    private static RepositoryProviderRegistration Registration(string name, Func<Func<IRepositoryProvider>> factory) => new()
    {
        DatabaseProvider = name,
        Prepare = _ => Result<Func<IRepositoryProvider>>.Success(factory())
    };

    private sealed class ProviderRecording : IRepositoryProvider
    {
        public ProviderRecording(string databaseProvider) => DatabaseProvider = databaseProvider;
        public string DatabaseProvider { get; }
        public int Disposals { get; private set; }
        public Task<Result<DbConnection>> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<DbConnection>.Failure("This recording provider does not open connections."));
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
