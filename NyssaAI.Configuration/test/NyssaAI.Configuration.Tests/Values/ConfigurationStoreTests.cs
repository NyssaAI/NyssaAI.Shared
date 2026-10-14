
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Values;
using NyssaAI.Configuration.Tests.Support;

namespace NyssaAI.Configuration.Tests.Values;
public sealed class ConfigurationStoreTests
{
    [Fact]
    public async Task SetAndGet_PlainValue_RoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);

        Assert.True((await store.SetAsync("service", "url", "https://example.test", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);

        var read = await store.GetAsync("service", "url", TestContext.Current.CancellationToken);
        Assert.True(read.IsSuccess);
        Assert.Equal("https://example.test", read.Value);
    }

    [Fact]
    public async Task Set_WithNoDestinationOnANewKey_UsesDefaultStorage()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);

        var written = await store.SetAsync("service", "token", "value", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccess);
        var stored = await ReadRawAsync(directory, "service", "token");
        Assert.True(stored.IsLiteral);
        Assert.False(stored.Encrypted);
        Assert.Equal("value", stored.Value);
    }

    [Fact]
    public async Task Set_WithNoDestinationOnAnExistingKey_KeepsTheProviderItAlreadyUses()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateSealingStore(directory);
        Assert.True((await store.SetAsync("service", "token", "s3cret", "aesLocal", TestContext.Current.CancellationToken)).IsSuccess);

        // Rotating a secret must not be able to downgrade it by omission.
        Assert.True((await store.SetAsync("service", "token", "rotated", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);

        var stored = await ReadRawAsync(directory, "service", "token");
        Assert.Equal("aesLocal", stored.Provider);
        Assert.True(stored.Encrypted);
        Assert.Equal("rotated", (await store.GetAsync("service", "token", TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task Set_NamingADifferentProvider_IsRefusedAsAMove()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateSealingStore(directory);
        Assert.True((await store.SetAsync("service", "token", "s3cret", "aesLocal", TestContext.Current.CancellationToken)).IsSuccess);

        var written = await store.SetAsync("service", "token", "s3cret", "otherProvider", TestContext.Current.CancellationToken);

        Assert.True(written.IsFailure);
        Assert.Equal(
            ConfigurationStore.AlreadyStoredElsewhere("service", "token", "aesLocal"),
            Assert.Single(written.Errors));
    }

    [Fact]
    public async Task Get_WithMissingKey_ReturnsNotFound()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);

        var read = await store.GetAsync("service", "absent", TestContext.Current.CancellationToken);

        Assert.True(read.IsFailure);
        Assert.Equal(ConfigurationStore.EntryNotFound("service", "absent"), Assert.Single(read.Errors));
    }

    [Fact]
    public async Task SetAndGet_SensitiveValue_RoundTripsThroughTheSecretProvider()
    {
        using var directory = new TemporaryDirectory();
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]);
        Assert.True(cipher.IsSuccess);
        var provider = new ConfigurationProviderLocalEncryption(cipher.Value);
        var store = ConfigurationStore.Create(
            new ConfigurationDocumentFactoryLocalFile(directory.Path),
            [provider]).Value;

        Assert.True((await store.SetAsync("service", "token", "s3cret", "aesLocal", TestContext.Current.CancellationToken)).IsSuccess);

        var read = await store.GetAsync("service", "token", TestContext.Current.CancellationToken);
        Assert.True(read.IsSuccess);
        Assert.Equal("s3cret", read.Value);
    }

    [Fact]
    public async Task Remove_DeletesOnlyTheRequestedEntry()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);
        await store.SetAsync("service", "one", "1", cancellationToken: TestContext.Current.CancellationToken);
        await store.SetAsync("service", "two", "2", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True((await store.RemoveAsync("service", "one", TestContext.Current.CancellationToken)).IsSuccess);

        var listed = await store.ListAsync("service", TestContext.Current.CancellationToken);
        Assert.True(listed.IsSuccess);
        Assert.Equal(["two"], listed.Value);
    }

    [Fact]
    public async Task SetAsAsync_ThenGetAsAsync_RoundTripsTypedValue()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);

        var written = await store.SetAsAsync(
            "service", "attempts", 42, cancellationToken: TestContext.Current.CancellationToken);
        var read = await store.GetAsAsync<int>(
            "service", "attempts", TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccess);
        Assert.True(read.IsSuccess);
        Assert.Equal(42, read.Value);
    }

    [Fact]
    public async Task RemoveAsync_WithLocalEncryption_RemovesEntryWithoutRemoteRetention()
    {
        using var directory = new TemporaryDirectory();
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var provider = new ConfigurationProviderLocalEncryption(cipher);
        var store = ConfigurationStore.Create(
            new ConfigurationDocumentFactoryLocalFile(directory.Path), [provider]).Value;
        Assert.True((await store.SetAsync("service", "token", "secret", provider.Name, TestContext.Current.CancellationToken)).IsSuccess);

        var removed = await store.RemoveAsync("service", "token", TestContext.Current.CancellationToken);
        var read = await store.GetAsync("service", "token", TestContext.Current.CancellationToken);

        Assert.True(removed.IsSuccess);
        Assert.False(removed.Value.RemoteValueRemains);
        Assert.True(read.IsFailure);
        Assert.Equal(NyssaAI.Results.ErrorCategory.NotFound, Assert.Single(read.Errors).Category);
    }

    [Fact]
    public void Create_WithDuplicateProviderNames_ReturnsValidation()
    {
        using var directory = new TemporaryDirectory();
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var first = new ConfigurationProviderLocalEncryption(cipher, "same");
        var second = new ConfigurationProviderLocalEncryption(cipher, "same");

        var created = ConfigurationStore.Create(
            new ConfigurationDocumentFactoryLocalFile(directory.Path), [first, second]);

        Assert.True(created.IsFailure);
        Assert.Equal(NyssaAI.Results.ErrorCategory.Conflict, Assert.Single(created.Errors).Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nested/namespace")]
    public async Task Get_WithNamespaceThatIsNotAFileName_ReturnsFailure(string @namespace)
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);

        var read = await store.GetAsync(@namespace, "key", TestContext.Current.CancellationToken);

        Assert.True(read.IsFailure);
        Assert.Equal(
            ConfigurationDocumentFactoryLocalFile.NamespaceNotAFileName(@namespace),
            Assert.Single(read.Errors));
    }

    [Fact]
    public async Task Move_ToPlain_RewritesTheEntryAndReportsTheSourceWithdrawn()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateSealingStore(directory);
        Assert.True((await store.SetAsync("service", "token", "s3cret", "aesLocal", TestContext.Current.CancellationToken)).IsSuccess);

        var moved = await store.MoveAsync("service", "token", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(moved.IsSuccess, moved.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal("aesLocal", moved.Value.From);
        Assert.Equal(string.Empty, moved.Value.To);
        // The ciphertext was the entry's own value, so overwriting the entry withdrew it.
        Assert.True(moved.Value.SourceWithdrawn);

        var stored = await ReadRawAsync(directory, "service", "token");
        Assert.True(stored.IsLiteral);
        Assert.Equal("s3cret", stored.Value);
    }

    [Fact]
    public async Task Move_FromPlainIntoAProvider_SealsTheValue()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateSealingStore(directory);
        Assert.True((await store.SetAsync("service", "token", "s3cret", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);

        var moved = await store.MoveAsync("service", "token", "aesLocal", TestContext.Current.CancellationToken);

        Assert.True(moved.IsSuccess, moved.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal(string.Empty, moved.Value.From);
        Assert.True(moved.Value.SourceWithdrawn);

        var stored = await ReadRawAsync(directory, "service", "token");
        Assert.Equal("aesLocal", stored.Provider);
        Assert.NotEqual("s3cret", stored.Value);
        Assert.Equal("s3cret", (await store.GetAsync("service", "token", TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task Move_ToTheProviderItAlreadyUses_ReturnsFailure()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateSealingStore(directory);
        Assert.True((await store.SetAsync("service", "token", "s3cret", "aesLocal", TestContext.Current.CancellationToken)).IsSuccess);

        var moved = await store.MoveAsync("service", "token", "aesLocal", TestContext.Current.CancellationToken);

        Assert.True(moved.IsFailure);
        Assert.Equal(ConfigurationStore.AlreadyThere("service", "token", "aesLocal"), Assert.Single(moved.Errors));
    }

    [Fact]
    public async Task MoveAsync_DefaultEntryWithoutDestination_ReturnsConflictAsync()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory);
        Assert.True((await store.SetAsync("service", "host", "localhost", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);

        var moved = await store.MoveAsync("service", "host", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(moved.IsFailure);
        Assert.Equal(ConfigurationStore.AlreadyThere("service", "host", string.Empty), Assert.Single(moved.Errors));
        Assert.Equal("localhost", (await store.GetAsync("service", "host", TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task Move_WhenTheSourceCannotWithdraw_SucceedsAndReportsTheCopyRemains()
    {
        using var directory = new TemporaryDirectory();
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var store = ConfigurationStore.Create(
            new ConfigurationDocumentFactoryLocalFile(directory.Path),
            [new UnwithdrawableProvider("stubborn"), new ConfigurationProviderLocalEncryption(cipher)]).Value;
        Assert.True((await store.SetAsync("service", "token", "s3cret", "stubborn", TestContext.Current.CancellationToken)).IsSuccess);

        var moved = await store.MoveAsync("service", "token", "aesLocal", TestContext.Current.CancellationToken);

        // The value is safely at its new home; the caller is told a copy survives rather than the
        // move reporting a failure it did not have.
        Assert.True(moved.IsSuccess, moved.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.False(moved.Value.SourceWithdrawn);
        Assert.Equal("s3cret", (await store.GetAsync("service", "token", TestContext.Current.CancellationToken)).Value);
    }

    private sealed class UnwithdrawableProvider(string name) : IConfigurationProvider
    {
        public string Name { get; } = name;

        public ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<string>.Success(entry.Value));

        public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ConfigurationValue>.Success(new ConfigurationValue { Value = value, Provider = Name }));

        public ValueTask<Result<ConfigurationRemoval>> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ConfigurationRemoval>.Failure(
                Error.FromMessages("no", "stubborn provider refuses to withdraw", ErrorCategory.Unexpected)));
    }

    private static ConfigurationStore CreateSealingStore(TemporaryDirectory directory) =>
        ConfigurationStore.Create(
            new ConfigurationDocumentFactoryLocalFile(directory.Path),
            [new ConfigurationProviderLocalEncryption(ConfigurationCipherAesGcm.Create(new byte[32]).Value)]).Value;

    private static async Task<ConfigurationValue> ReadRawAsync(TemporaryDirectory directory, string @namespace, string key)
    {
        var document = new ConfigurationDocumentFactoryLocalFile(directory.Path).Create(@namespace).Value;
        var entries = await document.ReadAsync(TestContext.Current.CancellationToken);
        return entries.Value[key];
    }

    private static ConfigurationStore CreateStore(TemporaryDirectory directory) =>
        new(new ConfigurationDocumentFactoryLocalFile(directory.Path));

}
