using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Values;

public sealed class ConfigurationRemovalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveAsync_PlainOrAbsentEntry_ReportsNoRemoteCopy(bool entryExists)
    {
        using var directory = new TemporaryDirectory();
        using var client = new ConfigurationClient(new ConfigurationStore(new ConfigurationDocumentFactoryLocalFile(directory.Path)));
        var cancellationToken = TestContext.Current.CancellationToken;
        if (entryExists)
            await client.SetAsync("mail", "password", "secret", cancellationToken: cancellationToken);

        var removed = await client.RemoveAsync("mail", "password", cancellationToken);
        var keys = await client.ListAsync("mail", cancellationToken);

        Assert.False(removed.RemoteValueRemains);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task RemoveAsync_LocalEntryWithInvalidEncryptionMetadata_RemovesWithoutReadingValue()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        using var client = new ConfigurationClient(new ConfigurationStore(documents));
        var cancellationToken = TestContext.Current.CancellationToken;
        await documents.Create("mail").Value.ModifyAsync(entries =>
        {
            entries["password"] = new ConfigurationValue { Value = "unreadable", Encrypted = true };
            return ValueTask.FromResult(Result.Success());
        }, cancellationToken);

        var removed = await client.RemoveAsync("mail", "password", cancellationToken);
        var keys = await client.ListAsync("mail", cancellationToken);

        Assert.False(removed.RemoteValueRemains);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task RemoveAsync_UnregisteredProvider_PreservesEntry()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var provider = new RemovalProvider();
        using var writer = new ConfigurationClient(ConfigurationStore.Create(documents, [provider]).Value);
        using var client = new ConfigurationClient(new ConfigurationStore(documents));
        var cancellationToken = TestContext.Current.CancellationToken;
        await writer.SetAsync("mail", "password", "secret", provider.Name, cancellationToken);
        var before = await File.ReadAllTextAsync(Path.Combine(directory.Path, "mail.json"), cancellationToken);

        var exception = await Assert.ThrowsAsync<ResultException>(() => client.RemoveAsync("mail", "password", cancellationToken).AsTask());
        var after = await File.ReadAllTextAsync(Path.Combine(directory.Path, "mail.json"), cancellationToken);

        Assert.Equal(ErrorCategory.Validation, Assert.Single(exception.Errors).Category);
        Assert.Equal(before, after);
        Assert.Equal("secret", provider.RemoteValue);
    }

    [Theory]
    [InlineData("returned")]
    [InlineData("thrown")]
    [InlineData("after-deletion")]
    [InlineData("before-call")]
    public async Task RemoveAsync_Cancellation_PreservesLocalEntryAndCallerToken(string when)
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new RemovalProvider { CancellationBehavior = when, Cancellation = cancellation };
        using var client = new ConfigurationClient(ConfigurationStore.Create(new ConfigurationDocumentFactoryLocalFile(directory.Path), [provider]).Value);
        await client.SetAsync("mail", "password", "secret", provider.Name, cancellation.Token);
        var path = Path.Combine(directory.Path, "mail.json");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        if (when == "before-call")
            cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RemoveAsync("mail", "password", cancellation.Token).AsTask());
        var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(before, after);
        Assert.Equal(when == "before-call" ? 0 : 1, provider.Removals);
        Assert.Equal(when == "after-deletion" ? null : "secret", provider.RemoteValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveAsync_LocalPublishFails_RetryCompletesAndReportsRetention(bool retain)
    {
        var documents = new ControlledDocument();
        var provider = new RemovalProvider { RetainOnRemoval = retain };
        using var client = new ConfigurationClient(ConfigurationStore.Create(documents, [provider]).Value);
        var cancellationToken = TestContext.Current.CancellationToken;
        await client.SetAsync("mail", "password", "secret", provider.Name, cancellationToken);
        documents.FailPublish = true;

        var exception = await Assert.ThrowsAsync<ResultException>(() => client.RemoveAsync("mail", "password", cancellationToken).AsTask());
        var keysAfterFailure = await client.ListAsync("mail", cancellationToken);
        var remoteAfterFailure = provider.RemoteValue;
        documents.FailPublish = false;
        var retried = await client.RemoveAsync("mail", "password", cancellationToken);
        var keysAfterRetry = await client.ListAsync("mail", cancellationToken);

        Assert.Same(ControlledDocument.PublishFailure, Assert.Single(exception.Errors));
        Assert.Equal(["password"], keysAfterFailure);
        Assert.Equal(retain ? "secret" : null, remoteAfterFailure);
        Assert.Equal(retain, retried.RemoteValueRemains);
        Assert.Empty(keysAfterRetry);
        Assert.Equal(2, provider.Removals);
    }

    private sealed class RemovalProvider : IConfigurationProvider
    {
        public string Name => "vault";
        public string? RemoteValue { get; private set; }
        public bool RetainOnRemoval { get; init; }
        public string? CancellationBehavior { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }
        public int Removals { get; private set; }

        public ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Removal must use the entry without reading the remote secret.");

        public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default)
        {
            RemoteValue = value;
            return ValueTask.FromResult(Result<ConfigurationValue>.Success(new ConfigurationValue { Value = "remote-pointer", Provider = Name }));
        }

        public ValueTask<Result<ConfigurationRemoval>> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default)
        {
            Removals++;
            if (CancellationBehavior == "returned")
                return ValueTask.FromResult(Result<ConfigurationRemoval>.Failure(Error.FromMessage(Error.CanceledUserMessage, ErrorCategory.Canceled)));
            if (CancellationBehavior == "thrown")
                throw new OperationCanceledException(cancellationToken);
            if (!RetainOnRemoval)
                RemoteValue = null;
            if (CancellationBehavior == "after-deletion")
                Cancellation!.Cancel();
            return ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = RemoteValue is not null }));
        }
    }

    private sealed class ControlledDocument : IConfigurationDocumentFactory, IConfigurationDocument
    {
        private Dictionary<string, ConfigurationValue> _entries = new(StringComparer.Ordinal);
        public static Error PublishFailure { get; } = Error.FromMessages("Save failed.", "Simulated document publish failure.", ErrorCategory.Unexpected);
        public bool FailPublish { get; set; }

        public Result<IConfigurationDocument> Create(string @namespace) => Result<IConfigurationDocument>.Success(this);

        public ValueTask<Result<IReadOnlyDictionary<string, ConfigurationValue>>> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IReadOnlyDictionary<string, ConfigurationValue>>.Success(_entries));

        public async ValueTask<Result> ModifyAsync(Func<IDictionary<string, ConfigurationValue>, ValueTask<Result>> change, CancellationToken cancellationToken = default)
        {
            var modified = new Dictionary<string, ConfigurationValue>(_entries, StringComparer.Ordinal);
            var changed = await change(modified);
            if (changed.IsFailure)
                return changed;
            if (FailPublish)
                return Result.Failure(PublishFailure);
            _entries = modified;
            return Result.Success();
        }
    }
}
