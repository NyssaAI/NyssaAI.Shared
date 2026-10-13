using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Repository.Configuration;
using NyssaAI.Results;
using Xunit;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.Tests.Configuration;

// This fixture deliberately covers omitted optional cancellation arguments and separately tests cancellation.
#pragma warning disable xUnit1051

public sealed class RepositoryConfigurationExtensionsTests : IDisposable
{
    private readonly ConfigurationHost _configurationHost = new();
    private readonly string _directory = Path.Combine(Directory.GetCurrentDirectory(), ".temp", "f001-config-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"DatabaseProvider\":\"sqlite\",\"ConnectionString\":null}")]
    [InlineData("{\"DatabaseProvider\":\" \",\"ConnectionString\":\"sentinel-secret\"}")]
    [InlineData("{\"sentinel-secret\":")]
    public async Task GetRepositoryConfigurationAsync_InvalidPayload_ReturnsSanitizedValidation(string json)
    {
        var configuration = Open();
        Assert.True((await configuration.SetAsync("nyssa.repository", "default", json, ConfigurationStore.Plain)).IsSuccess);
        var loaded = await configuration.GetRepositoryConfigurationAsync();
        Assert.True(loaded.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(loaded.Errors).Category);
        Assert.All(loaded.Errors, error =>
        {
            Assert.DoesNotContain("sentinel-secret", error.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-secret", error.TechnicalMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GetRepositoryConfigurationAsync_MissingEntry_ReturnsNotFoundWithoutProvisioning()
    {
        var configuration = Open();
        var loaded = await configuration.GetRepositoryConfigurationAsync();
        Assert.True(loaded.IsFailure);
        Assert.Equal(ErrorCategory.NotFound, Assert.Single(loaded.Errors).Category);
        var entries = await configuration.ListAsync("nyssa.repository");
        Assert.True(entries.IsSuccess);
        Assert.Empty(entries.Value);
    }

    [Fact]
    public async Task SetRepositoryConfigurationAsync_NamedKeys_RoundTripIndependently()
    {
        var configuration = Open();
        var first = Settings("sqlite", "Data Source=first.db");
        var second = Settings("postgresql", "Host=localhost;Password=sentinel-secret");
        Assert.True((await configuration.SetRepositoryConfigurationAsync(first, "first", ConfigurationStore.Plain)).IsSuccess);
        Assert.True((await configuration.SetRepositoryConfigurationAsync(second, "second", ConfigurationStore.Plain)).IsSuccess);
        var reopened = Open();
        var loadedFirst = await reopened.GetRepositoryConfigurationAsync("first");
        var loadedSecond = await reopened.GetRepositoryConfigurationAsync("second");
        Assert.True(loadedFirst.IsSuccess);
        Assert.True(loadedSecond.IsSuccess);
        Assert.Equal(first, loadedFirst.Value);
        Assert.Equal(second, loadedSecond.Value);
    }

    [Fact]
    public async Task SetRepositoryConfigurationAsync_NewEntryWithoutDestination_FailsWithoutWriting()
    {
        var configuration = Open();
        var saved = await configuration.SetRepositoryConfigurationAsync(Settings("sqlite", "Data Source=test.db"));
        Assert.True(saved.IsFailure);
        Assert.True((await configuration.GetRepositoryConfigurationAsync()).IsFailure);
    }

    [Theory]
    [InlineData("", "Data Source=test.db")]
    [InlineData("sqlite", " ")]
    public async Task SetRepositoryConfigurationAsync_InvalidFields_FailsBeforeWriting(string provider, string connection)
    {
        var configuration = Open();
        var saved = await configuration.SetRepositoryConfigurationAsync(Settings(provider, connection), storageProvider: ConfigurationStore.Plain);
        Assert.True(saved.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(saved.Errors).Category);
        Assert.Empty((await configuration.ListAsync("nyssa.repository")).Value);
    }

    [Fact]
    public async Task GetAndSetRepositoryConfigurationAsync_Canceled_ReturnCanceledWithoutWriting()
    {
        var configuration = Open();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var loaded = await configuration.GetRepositoryConfigurationAsync(cancellationToken: cancellation.Token);
        var saved = await configuration.SetRepositoryConfigurationAsync(Settings("sqlite", "Data Source=test.db"), storageProvider: ConfigurationStore.Plain, cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(loaded.Errors).Category);
        Assert.Equal(ErrorCategory.Canceled, Assert.Single(saved.Errors).Category);
        Assert.Empty((await configuration.ListAsync("nyssa.repository")).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAndSetRepositoryConfigurationAsync_InvalidKey_ReturnValidation(string key)
    {
        var configuration = Open();
        Assert.Equal(ErrorCategory.Validation, Assert.Single((await configuration.GetRepositoryConfigurationAsync(key)).Errors).Category);
        Assert.Equal(ErrorCategory.Validation, Assert.Single((await configuration.SetRepositoryConfigurationAsync(Settings("sqlite", "Data Source=test.db"), key)).Errors).Category);
    }

    private ConfigurationStore Open() => _configurationHost.Open(_directory);

    private static RepositoryConfiguration Settings(string provider, string connection) => new()
    {
        DatabaseProvider = provider,
        ConnectionString = connection
    };

    public void Dispose()
    {
        _configurationHost.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
