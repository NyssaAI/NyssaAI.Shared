using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Client;

public sealed class ConfigurationClientTests
{
    [Fact]
    public async Task GetAsync_MissingSetting_ThrowsWithNotFoundDetails()
    {
        using var directory = new TemporaryDirectory();
        using var client = new ConfigurationClient(new ConfigurationStore(new ConfigurationDocumentFactoryLocalFile(directory.Path)));

        var exception = await Assert.ThrowsAsync<ResultException>(() =>
            client.GetAsync("mail", "missing", TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(ErrorCategory.NotFound, Assert.Single(exception.Errors).Category);
    }

    [Fact]
    public async Task SetAsync_NewSettingWithoutProvider_StoresInDefaultStorage()
    {
        using var directory = new TemporaryDirectory();
        using var client = new ConfigurationClient(new ConfigurationStore(new ConfigurationDocumentFactoryLocalFile(directory.Path)));

        await client.SetAsync("mail", "host", "smtp.example.test", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("smtp.example.test", await client.GetAsync("mail", "host", TestContext.Current.CancellationToken));
        var document = await File.ReadAllTextAsync(Path.Combine(directory.Path, "mail.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\"provider\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("\"encrypted\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reauth_required\"", document, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailure_ThrowsWithOriginalDetails(bool writing)
    {
        using var directory = new TemporaryDirectory();
        var provider = new FailingProvider();
        using var client = CreateClient(directory, provider);
        await client.SetAsync("mail", "password", "secret", provider.Name, TestContext.Current.CancellationToken);
        provider.Failure = Error.FromMessages("Vault unavailable.", "Remote connection failed.", ErrorCategory.Unexpected);

        var exception = await Assert.ThrowsAsync<ResultException>(async () =>
        {
            if (writing)
                await client.SetAsync("mail", "password", "updated", cancellationToken: TestContext.Current.CancellationToken);
            else
                await client.GetAsync("mail", "password", TestContext.Current.CancellationToken);
        });

        Assert.Same(provider.Failure, Assert.Single(exception.Errors));
        Assert.Equal("Vault unavailable.", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderCancellation_ThrowsOperationCanceledException(bool writing)
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new FailingProvider();
        using var client = CreateClient(directory, provider);
        await client.SetAsync("mail", "password", "secret", provider.Name, cancellation.Token);
        provider.Failure = Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (writing)
                await client.SetAsync("mail", "password", "updated", cancellationToken: cancellation.Token);
            else
                await client.GetAsync("mail", "password", cancellation.Token);
        });

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task SetAsync_AlreadyCanceled_DoesNotReachProvider()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var provider = new FailingProvider();
        using var client = CreateClient(directory, provider);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SetAsync("mail", "password", "secret", provider.Name, cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, provider.Writes);
        Assert.False(File.Exists(Path.Combine(directory.Path, "mail.json")));
    }

    private static ConfigurationClient CreateClient(TemporaryDirectory directory, IConfigurationProvider provider) =>
        new(ConfigurationStore.Create(new ConfigurationDocumentFactoryLocalFile(directory.Path), [provider]).ThrowIfError());

    private sealed class FailingProvider : IConfigurationProvider
    {
        public string Name => "vault";
        public Error? Failure { get; set; }
        public int Writes { get; private set; }

        public ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Failure is { } error ? Result<string>.Failure(error) : Result<string>.Success(entry.Value));

        public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default)
        {
            Writes++;
            return ValueTask.FromResult(Failure is { } error
                ? Result<ConfigurationValue>.Failure(error)
                : Result<ConfigurationValue>.Success(new ConfigurationValue { Value = value, Provider = Name }));
        }

        public ValueTask<Result<ConfigurationRemoval>> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false }));
    }
}
