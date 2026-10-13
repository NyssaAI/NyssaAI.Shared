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
        Assert.True((await configuration.SetRepositoryConfigurationAsync(settings, key, "aesLocal", cancellationToken)).IsSuccess);
        var services = new ServiceCollection();

        var added = namedKey
            ? await services.AddNyssaRepositoryAsync(configuration, key: key, cancellationToken: cancellationToken)
            : await services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken);

        Assert.True(added.IsSuccess, added.ToString());
        Assert.Equal(3, services.Count);
        Assert.Single(services, service => service.ServiceType == typeof(IMigrationRunner));
        Assert.False(File.Exists(database));
        var replacement = settings with { ConnectionString = $"Data Source={Path.Combine(_directory, "replacement.db")}" };
        Assert.True((await configuration.SetRepositoryConfigurationAsync(replacement, key, cancellationToken: cancellationToken)).IsSuccess);
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var provider = container.GetRequiredService<IRepositoryProvider>();
        Assert.Same(provider, container.GetRequiredService<IRepositoryProvider>());
        Assert.False(File.Exists(database));
        var opened = await provider.OpenConnectionAsync(cancellationToken);
        Assert.True(opened.IsSuccess, opened.ToString());
        await using var connection = opened.Value;
        Assert.Equal(database, connection.DataSource);
        Assert.False(File.Exists(Path.Combine(_directory, "replacement.db")));
    }

    [Theory]
    [InlineData(null, ErrorCategory.NotFound)]
    [InlineData("{sentinel-secret", ErrorCategory.Validation)]
    [InlineData("{}", ErrorCategory.Validation)]
    [InlineData("{\"DatabaseProvider\":\"unknown\",\"ConnectionString\":\"sentinel-secret\"}", ErrorCategory.Validation)]
    [InlineData("{\"DatabaseProvider\":\"sqlite\",\"ConnectionString\":\"sentinel-secret\"}", ErrorCategory.Validation)]
    public async Task AddNyssaRepositoryAsync_UnusableSettings_ThrowsSanitizedFailureWithoutRegistration(string? json, ErrorCategory category)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = Open();
        if (json is not null)
            Assert.True((await configuration.SetAsync("nyssa.repository", "default", json, ConfigurationStore.Plain, cancellationToken)).IsSuccess);
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var original = services.ToArray();

        var errors = (await Assert.ThrowsAsync<ResultException>(() => services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken))).Errors;

        Assert.Equal(category, Assert.Single(errors).Category);
        Assert.Equal(original, services.ToArray());
        Assert.DoesNotContain("sentinel-secret", errors[0].UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", errors[0].TechnicalMessage, StringComparison.Ordinal);
        if (json is null)
            Assert.Empty((await configuration.ListAsync("nyssa.repository", cancellationToken)).Value);
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_WrongEncryptionKey_ThrowsWithoutRegistration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var protectedConfiguration = Open();
        Assert.True((await protectedConfiguration.SetRepositoryConfigurationAsync(Settings(), storageProvider: "aesLocal", cancellationToken: cancellationToken)).IsSuccess);
        var configuration = _configurationHost.Open(_directory, differentKey: true);
        var expected = await configuration.GetRepositoryConfigurationAsync(cancellationToken: cancellationToken);
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

        var added = await Assert.ThrowsAsync<ResultException>(() => services.AddNyssaRepositoryAsync(Open(), key, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCategory.Validation, Assert.Single(added.Errors).Category);
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
        Assert.True((await configuration.SetRepositoryConfigurationAsync(Settings(), storageProvider: ConfigurationStore.Plain, cancellationToken: cancellationToken)).IsSuccess);
        var noServices = await Assert.ThrowsAsync<ResultException>(() => missingServices.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellationToken));
        var noConfiguration = await Assert.ThrowsAsync<ResultException>(() => services.AddNyssaRepositoryAsync(missingConfiguration, cancellationToken: cancellationToken));

        Assert.Equal(ErrorCategory.Validation, Assert.Single(noServices.Errors).Category);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(noConfiguration.Errors).Category);
        Assert.Empty(services);
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_AlreadyRegistered_ThrowsConflictAfterLoading()
    {
        var configuration = Open();
        Assert.True((await configuration.SetRepositoryConfigurationAsync(Settings(), storageProvider: ConfigurationStore.Plain, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        var services = new ServiceCollection();
        Assert.True(services.AddNyssaRepository(Settings()).IsSuccess);
        var original = services.ToArray();

        var added = await Assert.ThrowsAsync<ResultException>(() => services.AddNyssaRepositoryAsync(configuration, cancellationToken: TestContext.Current.CancellationToken));

        // Loaded settings reach the same duplicate-registration check as the DTO overload.
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(added.Errors).Category);
        Assert.Equal(original, services.ToArray());
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_PreCanceled_ThrowsCanceledWithoutRegistration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var services = new ServiceCollection();

        var added = await Assert.ThrowsAsync<ResultException>(() => services.AddNyssaRepositoryAsync(Open(), cancellationToken: cancellation.Token));

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(added.Errors).Category);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddNyssaRepositoryAsync_CancellationDuringRead_DoesNotRegister(bool throwOnCancellation)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
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
        Assert.True((await configuration.SetRepositoryConfigurationAsync(Settings(), storageProvider: storage.Name, cancellationToken: cancellation.Token)).IsSuccess);
        var services = new ServiceCollection();

        var pending = services.AddNyssaRepositoryAsync(configuration, cancellationToken: cancellation.Token);
        await readStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        Assert.Empty(services);
        cancellation.Cancel();
        readFinished.SetResult();
        var added = await Assert.ThrowsAsync<ResultException>(() => pending);

        Assert.Equal(ErrorCategory.Canceled, Assert.Single(added.Errors).Category);
        Assert.Empty(services);
    }

    [Fact]
    public async Task AddNyssaRepositoryAsync_CustomDatabaseProvider_ForwardsRegistrationAndDefersConstruction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = Open();
        var settings = Settings() with { DatabaseProvider = "custom" };
        Assert.True((await configuration.SetRepositoryConfigurationAsync(settings, storageProvider: ConfigurationStore.Plain, cancellationToken: cancellationToken)).IsSuccess);
        var prepared = 0;
        var constructed = 0;
        var registration = new RepositoryProviderRegistration
        {
            DatabaseProvider = "custom",
            Prepare = loaded =>
            {
                prepared++;
                Assert.Equal(settings, loaded);
                return Result<Func<IRepositoryProvider>>.Success(() =>
                {
                    constructed++;
                    return RepositoryProviderSqlite.Prepare(Settings()).Value();
                });
            }
        };
        var services = new ServiceCollection();

        var added = await services.AddNyssaRepositoryAsync(configuration, providers: [registration], cancellationToken: cancellationToken);

        Assert.True(added.IsSuccess, added.ToString());
        Assert.Equal(1, prepared);
        Assert.Equal(0, constructed);
        await using var container = services.BuildServiceProvider();
        _ = container.GetRequiredService<IRepositoryProvider>();
        Assert.Equal(1, constructed);
    }

    [Fact]
    public void AddNyssaRepository_ExistingRepository_ThrowsWithoutChangingCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepository>(_ => null!);
        var original = services.ToArray();
        var failure = Assert.Throws<ResultException>(() => services.AddNyssaRepository(Settings()));
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(failure.Errors).Category);
        Assert.Equal(original, services.ToArray());
    }

    [Fact]
    public void AddNyssaRepository_NullServices_ThrowsValidationBeforePreparation()
    {
        // Deliberately exercise runtime validation despite the non-nullable signature.
        IServiceCollection services = null!;
        var exception = Assert.Throws<ResultException>(() => services.AddNyssaRepository(Settings()));
        var error = Assert.Single(exception.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Equal("Unable to register the data repository", error.UserMessage);
        Assert.Equal("The collection cannot be null.", error.TechnicalMessage);
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
                return Result<Func<IRepositoryProvider>>.Success(() =>
                {
                    constructed++;
                    return RepositoryProviderSqlite.Prepare(Settings()).Value();
                });
            }
        };

        var result = services.AddNyssaRepository(() =>
        {
            calls++;
            return Settings() with { DatabaseProvider = "custom" };
        }, [registration]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, calls);
        Assert.Equal(1, prepared);
        Assert.Equal(0, constructed);
        await using var container = services.BuildServiceProvider();
        Assert.Same(container.GetRequiredService<IRepositoryProvider>(), container.GetRequiredService<IRepositoryProvider>());
        Assert.Equal(1, constructed);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("result")]
    [InlineData("invalid")]
    [InlineData("throws")]
    public void AddNyssaRepository_InvalidConfigurationFactory_ThrowsWithoutRegistration(string scenario)
    {
        var services = new ServiceCollection();
        Func<RepositoryConfiguration> configure = scenario switch
        {
            "null" => null!,
            "result" => () => null!,
            "invalid" => () => Settings() with { DatabaseProvider = " " },
            _ => () => throw new InvalidOperationException("sentinel-secret")
        };

        var exception = Assert.Throws<ResultException>(() => services.AddNyssaRepository(configure));

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
        public ValueTask<Result> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Failure("This test vault does not support withdrawal."));
    }
}
