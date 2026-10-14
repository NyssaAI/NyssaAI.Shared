using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Repository.Migrations;
using NyssaAI.Repository.Providers;
using NyssaAI.Results;
using Xunit;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.Tests.Client;

public sealed class RepositoryServiceCollectionExtensionsTests : IDisposable
{
    private readonly ConfigurationHost _configurationHost = new();
    private readonly string _directory = Path.Combine(Directory.GetCurrentDirectory(), ".temp", "repository-registration", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddNyssaRepositoryAsync_ProtectedSettings_RegistersSelectedSnapshot(bool namedKey)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = Open();
        var key = namedKey ? "reporting" : "default";
        var database = Path.Combine(_directory, "selected.db");
        var settings = new RepositoryConfiguration { DatabaseProvider = "sqlite", ConnectionString = $"Data Source={database}" };
        await configuration.SetRepositoryConfigurationAsync(settings, key, "aesLocal", cancellationToken);
        var services = new ServiceCollection();

        var added = namedKey
            ? await services.AddNyssaRepositoryAsync(configuration, key: key, cancellationToken: cancellationToken)
            : await services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken);

        Assert.Equal(4, services.Count);
        Assert.Single(services, service => service.ServiceType == typeof(IMigrationRunner));
        Assert.False(File.Exists(database));
        var replacement = settings with { ConnectionString = $"Data Source={Path.Combine(_directory, "replacement.db")}" };
        await configuration.SetRepositoryConfigurationAsync(replacement, key, cancellationToken: cancellationToken);
        await using (var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            var provider = container.GetRequiredService<IRepositoryProvider>();
            Assert.Same(provider, container.GetRequiredService<IRepositoryProvider>());
            Assert.False(File.Exists(database));
            var opened = await provider.OpenConnectionAsync(cancellationToken);

            await using (var connection = opened)
            {
                Assert.Equal(database, connection.DataSource);
                Assert.False(File.Exists(Path.Combine(_directory, "replacement.db")));
            }
        }
    }

    [Theory]
    [InlineData(null, typeof(ResultException))]
    [InlineData("{sentinel-secret", typeof(ResultException))]
    [InlineData("{}", typeof(ResultException))]
    [InlineData("{\"DatabaseProvider\":\"unknown\",\"ConnectionString\":\"sentinel-secret\"}", typeof(InvalidOperationException))]
    [InlineData("{\"DatabaseProvider\":\"sqlite\",\"ConnectionString\":\"sentinel-secret\"}", typeof(ArgumentException))]
    public async Task AddNyssaRepositoryAsync_UnusableSettings_ThrowsSanitizedFailureWithoutRegistration(string? json, Type exceptionType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = Open();
        if (json is not null)
            await configuration.SetAsync("nyssa.repository", "default", json, cancellationToken: cancellationToken);
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var original = services.ToArray();

        await Assert.ThrowsAsync(exceptionType, () => services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken));

        Assert.Equal(original, services.ToArray());
        if (json is null)
            Assert.Empty(await configuration.ListAsync("nyssa.repository", cancellationToken));
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_WrongEncryptionKey_ThrowsWithoutRegistration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var protectedConfiguration = Open();
        await protectedConfiguration.SetRepositoryConfigurationAsync(Settings(), storageProvider: "aesLocal", cancellationToken: cancellationToken);
        var configuration = _configurationHost.Open(_directory, differentKey: true);
        var expected = await Assert.ThrowsAsync<ResultException>(() => configuration.GetRepositoryConfigurationAsync(cancellationToken: cancellationToken));
        var services = new ServiceCollection();

        var added = await Assert.ThrowsAsync<ResultException>(() => services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken));

        Assert.Equal(expected.Errors, added.Errors);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AddNyssaRepositoryAsync_InvalidKey_ThrowsValidationWithoutRegistration(string key)
    {
        var services = new ServiceCollection();

        var added = await Assert.ThrowsAsync<ArgumentException>(() => services.AddNyssaRepositoryAsync(Open(), key, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(services);
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_NullArguments_ThrowsValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Nulls deliberately exercise runtime boundary validation despite non-nullable signatures.
        IServiceCollection missingServices = null!;
        ConfigurationStore missingConfiguration = null!;
        var services = new ServiceCollection();

        var configuration = Open();
        await configuration.SetRepositoryConfigurationAsync(Settings(), cancellationToken: cancellationToken);
        var noServices = await Assert.ThrowsAsync<ArgumentNullException>(() => missingServices.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken));
        var noConfiguration = await Assert.ThrowsAsync<ArgumentNullException>(() => services.AddNyssaRepositoryAsync(missingConfiguration, cancellationToken: cancellationToken));


        Assert.Empty(services);
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_AlreadyRegistered_ThrowsConflictAfterLoading()
    {
        var configuration = Open();
        await configuration.SetRepositoryConfigurationAsync(Settings(), cancellationToken: TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        _ = services.AddNyssaRepository(Settings());
        var original = services.ToArray();

        var added = await Assert.ThrowsAsync<InvalidOperationException>(() => services.AddNyssaRepositoryAsync(configuration, cancellationToken: TestContext.Current.CancellationToken));

        // Loaded settings reach the same duplicate-registration check as the DTO overload.

        Assert.Equal(original, services.ToArray());
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_PreCanceled_ThrowsCanceledWithoutRegistration()
    {
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var services = new ServiceCollection();

            var added = await Assert.ThrowsAsync<OperationCanceledException>(() => services.AddNyssaRepositoryAsync(Open(), cancellationToken: cancellation.Token));

            Assert.Equal(cancellation.Token, added.CancellationToken);
            Assert.Empty(services);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddNyssaRepositoryAsync_CancellationDuringRead_DoesNotRegister(bool throwOnCancellation)
    {
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var storage = new ConfigurationProviderReadControlled(async (value, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                readStarted.SetResult();
                await readFinished.Task.WaitAsync(TestContext.Current.CancellationToken);
                if (throwOnCancellation)
                    token.ThrowIfCancellationRequested();
                return Result<string>.Success(value);
            });
            var configuration = _configurationHost.Open(_directory, vault: storage);
            await configuration.SetRepositoryConfigurationAsync(Settings(), storageProvider: storage.Name, cancellationToken: cancellation.Token);
            var services = new ServiceCollection();

            var pending = services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellation.Token);
            await readStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted);
            Assert.Empty(services);
            cancellation.Cancel();
            readFinished.SetResult();
            var added = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);

            Assert.Equal(cancellation.Token, added.CancellationToken);
            Assert.Empty(services);
        }
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_CustomDatabaseProvider_ForwardsRegistrationAndDefersConstruction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = Open();
        var settings = Settings() with { DatabaseProvider = "custom" };
        await configuration.SetRepositoryConfigurationAsync(settings, cancellationToken: cancellationToken);
        var prepared = 0;
        var constructed = 0;
        var registration = new RepositoryProviderRegistration
        {
            DatabaseProvider = "custom",
            Prepare = loaded =>
            {
                prepared++;
                Assert.Equal(settings, loaded);
                return () =>
                {
                    constructed++;
                    return RepositoryProviderSqlite.Prepare(Settings())();
                };
            }
        };
        var services = new ServiceCollection();

        var added = await services.AddNyssaRepositoryAsync(configuration, providers: [registration], cancellationToken: cancellationToken);

        Assert.Equal(1, prepared);
        Assert.Equal(0, constructed);
        await using (var container = services.BuildServiceProvider())
        {
            _ = container.GetRequiredService<IRepositoryProvider>();
            Assert.Equal(1, constructed);
        }
    }

    [Fact]
    public void AddNyssaRepository_ExistingRepository_ThrowsWithoutChangingCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepository>(_ => null!);
        var original = services.ToArray();
        var failure = Assert.Throws<InvalidOperationException>(() => services.AddNyssaRepository(Settings()));

        Assert.Equal(original, services.ToArray());
    }

    [Fact]
    public void AddNyssaRepository_NullServices_ThrowsValidationBeforePreparation()
    {
        // Deliberately exercise runtime validation despite the non-nullable signature.
        IServiceCollection services = null!;
        var exception = Assert.Throws<ArgumentNullException>(() => services.AddNyssaRepository(Settings()));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public async Task AddNyssaRepository_ConfigurationFactory_DelegatesAndDefersConstruction()
    {
        var calls = 0;
        var prepared = 0;
        var constructed = 0;
        var services = new ServiceCollection();
        var registration = new RepositoryProviderRegistration
        {
            DatabaseProvider = "custom",
            Prepare = settings =>
            {
                prepared++;
                Assert.Equal("custom", settings.DatabaseProvider);
                return () =>
                {
                    constructed++;
                    return RepositoryProviderSqlite.Prepare(Settings())();
                };
            }
        };

        var result = services.AddNyssaRepository(() =>
        {
            calls++;
            return Settings() with { DatabaseProvider = "custom" };
        }, [registration]);

        Assert.Equal(1, calls);
        Assert.Equal(1, prepared);
        Assert.Equal(0, constructed);
        await using (var container = services.BuildServiceProvider())
        {
            Assert.Same(container.GetRequiredService<IRepositoryProvider>(), container.GetRequiredService<IRepositoryProvider>());
            Assert.Equal(1, constructed);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("result")]
    [InlineData("invalid")]
    [InlineData("throws")]
    public void AddNyssaRepository_InvalidConfigurationFactory_ThrowsWithoutRegistration(string scenario)
    {
        var services = new ServiceCollection();
        var expected = new InvalidOperationException("configuration callback failed");
        Func<RepositoryConfiguration> configure = scenario switch
        {
            "null" => null!,
            "result" => () => null!,
            "invalid" => () => Settings() with { DatabaseProvider = " " },
            _ => () => throw expected
        };

        var exceptionType = scenario == "null" ? typeof(ArgumentNullException) : scenario == "invalid" ? typeof(ArgumentException) : typeof(InvalidOperationException);
        var exception = Assert.Throws(exceptionType, () => services.AddNyssaRepository(configure));
        if (scenario == "throws") Assert.Same(expected, exception);

        Assert.DoesNotContain("sentinel-secret", exception.Message);
        Assert.Empty(services);
    }

    private ConfigurationStore Open() => _configurationHost.Open(_directory);

    private static RepositoryConfiguration Settings() => new() { DatabaseProvider = "sqlite", ConnectionString = "Data Source=:memory:" };

    public void Dispose()
    {
        _configurationHost.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class ConfigurationProviderReadControlled : ICloudKeyVaultFactory, ICloudKeyVault
    {
        private readonly Func<string, CancellationToken, Task<Result<string>>> _read;

        public ConfigurationProviderReadControlled(Func<string, CancellationToken, Task<Result<string>>> read) => _read = read;
        private string _value = string.Empty;
        public string Name => "controlled";
        public string Kind => Name;
        public OAuthOptions? Authorization(VaultOptions settings) => null;
        public Result<ICloudKeyVault> Create(VaultOptions settings, IAccessTokenSource? tokens) => Result<ICloudKeyVault>.Success(this);
        public async ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
        {
            var read = await _read(_value, cancellationToken);
            return read.IsFailure ? Result<string?>.Failure(read.Errors) : Result<string?>.Success(read.Value);
        }
        public ValueTask<Result<ConfigurationPointer>> WriteAsync(string @namespace, string key, string value, CancellationToken cancellationToken = default)
        {
            _value = value;
            return ValueTask.FromResult(ConfigurationPointer.Parse(Name, $"{@namespace}/{key}"));
        }
        public ValueTask<Result<ConfigurationRemoval>> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ConfigurationRemoval>.Failure("This test vault does not support withdrawal."));
    }
}
