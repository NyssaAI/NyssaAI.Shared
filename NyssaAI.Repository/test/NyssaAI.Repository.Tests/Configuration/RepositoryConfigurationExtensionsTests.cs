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
    [InlineData("null", typeof(ResultException))]
    [InlineData("{}", typeof(ResultException))]
    [InlineData("{\"DatabaseProvider\":\"sqlite\",\"ConnectionString\":null}", typeof(ArgumentNullException))]
    [InlineData("{\"DatabaseProvider\":\" \",\"ConnectionString\":\"sentinel-secret\"}", typeof(ArgumentException))]
    [InlineData("{\"sentinel-secret\":", typeof(ResultException))]
    public async Task GetRepositoryConfigurationAsync_InvalidPayload_ThrowsOwnedOrConfigurationException(string json, Type exceptionType)
    {
        var configuration = Open();
        await configuration.SetAsync("nyssa.repository", "default", json);
        var loaded = await Assert.ThrowsAsync(exceptionType, () => configuration.GetRepositoryConfigurationAsync());

    }

    [Fact]
    public async Task GetRepositoryConfigurationAsync_MissingEntry_ReturnsNotFoundWithoutProvisioning()
    {
        var configuration = Open();
        var loaded = await Assert.ThrowsAsync<ResultException>(() => configuration.GetRepositoryConfigurationAsync());

        Assert.Equal(ErrorCategory.NotFound, Assert.Single(loaded.Errors).Category);
        var entries = await configuration.ListAsync("nyssa.repository");
        Assert.Empty(entries);
    }

    [Fact]
    public async Task SetRepositoryConfigurationAsync_NamedKeys_RoundTripIndependently()
    {
        var configuration = Open();
        var first = Settings("sqlite", "Data Source=first.db");
        var second = Settings("postgresql", "Host=localhost;Password=sentinel-secret");
        await configuration.SetRepositoryConfigurationAsync(first, "first");
        await configuration.SetRepositoryConfigurationAsync(second, "second");
        var reopened = Open();
        var loadedFirst = await reopened.GetRepositoryConfigurationAsync("first");
        var loadedSecond = await reopened.GetRepositoryConfigurationAsync("second");

        Assert.Equal(first, loadedFirst);
        Assert.Equal(second, loadedSecond);
    }

    [Fact]
    public async Task SetRepositoryConfigurationAsync_NewEntryWithoutDestination_UsesDefaultStorage()
    {
        var configuration = Open();
        await configuration.SetRepositoryConfigurationAsync(Settings("sqlite", "Data Source=test.db"));
        var loaded = await configuration.GetRepositoryConfigurationAsync();

        Assert.Equal(Settings("sqlite", "Data Source=test.db"), loaded);
    }

    [Theory]
    [InlineData("", "Data Source=test.db")]
    [InlineData("sqlite", " ")]
    public async Task SetRepositoryConfigurationAsync_InvalidFields_FailsBeforeWriting(string provider, string connection)
    {
        var configuration = Open();
        var saved = await Assert.ThrowsAsync<ArgumentException>(() => configuration.SetRepositoryConfigurationAsync(Settings(provider, connection)));
        Assert.Empty(await configuration.ListAsync("nyssa.repository"));
    }

    [Fact]
    public async Task GetAndSetRepositoryConfigurationAsync_Canceled_ReturnCanceledWithoutWriting()
    {
        var configuration = Open();
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var loaded = await Assert.ThrowsAsync<OperationCanceledException>(() => configuration.GetRepositoryConfigurationAsync(cancellationToken: cancellation.Token));
            var saved = await Assert.ThrowsAsync<OperationCanceledException>(() => configuration.SetRepositoryConfigurationAsync(Settings("sqlite", "Data Source=test.db"), cancellationToken: cancellation.Token));
            Assert.Equal(cancellation.Token, loaded.CancellationToken);
            Assert.Equal(cancellation.Token, saved.CancellationToken);
            Assert.Empty(await configuration.ListAsync("nyssa.repository"));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetAndSetRepositoryConfigurationAsync_InvalidKey_ReturnValidation(string key)
    {
        var configuration = Open();
        await Assert.ThrowsAsync<ArgumentException>(() => configuration.GetRepositoryConfigurationAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => configuration.SetRepositoryConfigurationAsync(Settings("sqlite", "Data Source=test.db"), key));
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
