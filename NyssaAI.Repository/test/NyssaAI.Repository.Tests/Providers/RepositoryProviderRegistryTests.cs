using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.Providers;
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
        Assert.Equal(0, created);
        await using (var provider = result())
        {
            Assert.Equal("custom", provider.DatabaseProvider);
            Assert.Equal(1, prepared);
            Assert.Equal(1, created);
        }
    }

    [Theory]
    [InlineData("custom", " CUSTOM ")]
    [InlineData("unknown", "custom")]
    [InlineData("custom", " ")]
    public void RegisterDatabaseProviders_InvalidRegistry_FailsBeforePreparation(string selection, string secondName)
    {
        var calls = 0;
        Func<Func<IRepositoryProvider>> factory = () => { calls++; return () => new ProviderRecording("custom"); };
        Assert.Throws(secondName == " " ? typeof(ArgumentException) : typeof(InvalidOperationException),
            () => RepositoryProviderRegistry.RegisterDatabaseProviders(Settings(selection), [Registration("custom", factory), Registration(secondName, factory)]));
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
        _ = services.AddNyssaRepository(Settings("custom"), registrations);
        var count = services.Count;
        var repeated = Assert.Throws<InvalidOperationException>(() => services.AddNyssaRepository(Settings("custom"), registrations));

        Assert.Equal(count, services.Count);
    }

    [Fact]
    public void RegisterDatabaseProviders_PreparationFailure_PreservesExceptionInstance()
    {
        var expected = new InvalidOperationException("preparation failure");
        var registration = new RepositoryProviderRegistration
        {
            DatabaseProvider = "custom",
            Prepare = _ => throw expected
        };
        var result = Assert.Throws<InvalidOperationException>(() => RepositoryProviderRegistry.RegisterDatabaseProviders(Settings("custom"), [registration]));
        Assert.Same(expected, result);

        Assert.DoesNotContain("sentinel-secret", result.Message, StringComparison.Ordinal);
    }

    private static RepositoryConfiguration Settings(string provider) => new() { DatabaseProvider = provider, ConnectionString = "sentinel-secret" };

    private static RepositoryProviderRegistration Registration(string name, Func<Func<IRepositoryProvider>> factory) => new()
    {
        DatabaseProvider = name,
        Prepare = _ => factory()
    };

    private sealed class ProviderRecording : IRepositoryProvider
    {
        public ProviderRecording(string databaseProvider) => DatabaseProvider = databaseProvider;
        public string DatabaseProvider { get; }
        public int Disposals { get; private set; }
        public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This recording provider does not open connections.");
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
