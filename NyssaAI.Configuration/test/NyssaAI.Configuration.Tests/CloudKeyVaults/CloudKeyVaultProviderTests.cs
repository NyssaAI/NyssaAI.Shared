using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.CloudKeyVaults;

public sealed class CloudKeyVaultProviderTests
{
    [Fact]
    public async Task WriteAsync_RecordsThePointerTheStoreReturnedRatherThanTheValue()
    {
        var vault = new FakeVault();
        var provider = new CloudKeyVaultProvider(vault, "bitwarden");

        var written = await provider.WriteAsync("s3cret", "db", "password", TestContext.Current.CancellationToken);

        Assert.True(written.IsSuccess, written.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal("bitwarden", written.Value.Provider);
        Assert.False(written.Value.Encrypted);
        Assert.DoesNotContain("s3cret", written.Value.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_ResolvesTheEntryThroughTheAddressItRecords()
    {
        var vault = new FakeVault();
        var provider = new CloudKeyVaultProvider(vault, "bitwarden");
        var cancellationToken = TestContext.Current.CancellationToken;
        var written = await provider.WriteAsync("s3cret", "db", "password", cancellationToken);

        var read = await provider.ReadAsync(written.Value, "db", "password", cancellationToken);

        Assert.True(read.IsSuccess, read.Errors.FirstOrDefault()?.TechnicalMessage);
        Assert.Equal("s3cret", read.Value);
    }

    [Fact]
    public async Task ReadAsync_ReportsABrokenReferenceWhenTheStoreHoldsNothingThere()
    {
        var provider = new CloudKeyVaultProvider(new FakeVault(), "bitwarden");
        var entry = new ConfigurationValue { Value = "db/password", Provider = "bitwarden" };

        var read = await provider.ReadAsync(entry, "db", "password", TestContext.Current.CancellationToken);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, read.Errors[0].Category);
    }

    [Fact]
    public async Task ReadAsync_RefusesAnEntryWhoseValueIsNotAnAddress()
    {
        var provider = new CloudKeyVaultProvider(new FakeVault(), "bitwarden");
        var entry = new ConfigurationValue { Value = "not-an-address", Provider = "bitwarden" };

        var read = await provider.ReadAsync(entry, "db", "password", TestContext.Current.CancellationToken);

        Assert.True(read.IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EraseAsync_StoredPointer_DeletesItsAddressAndPreservesRetentionFlag(bool retain)
    {
        var vault = new FakeVault { RetainOnRemoval = retain };
        var provider = new CloudKeyVaultProvider(vault, "bitwarden");
        var cancellationToken = TestContext.Current.CancellationToken;
        var written = await provider.WriteAsync("secret", "db", "password", cancellationToken);

        var erased = await provider.EraseAsync(written.Value, "db", "password", cancellationToken);

        Assert.True(erased.IsSuccess);
        Assert.Equal("secrets/1", vault.RemovedPointer);
        Assert.Equal(retain, erased.Value.RemoteValueRemains);
        Assert.Equal(retain, vault.Secrets.ContainsKey("secrets/1"));
    }

    [Fact]
    public async Task EraseAsync_InvalidPointer_RejectsWithoutContactingVault()
    {
        var vault = new FakeVault();
        var provider = new CloudKeyVaultProvider(vault, "bitwarden");
        var entry = new ConfigurationValue { Value = "not-an-address", Provider = "bitwarden" };

        var erased = await provider.EraseAsync(entry, "db", "password", TestContext.Current.CancellationToken);

        Assert.True(erased.IsFailure);
        Assert.Null(vault.RemovedPointer);
    }

    // Files secrets under an identifier of its own, which is what a real store does and what the
    // provider must not assume away.
    private sealed class FakeVault : ICloudKeyVault
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);
        private int _next;

        public IReadOnlyDictionary<string, string> Secrets => _secrets;
        public bool RetainOnRemoval { get; init; }
        public string? RemovedPointer { get; private set; }

        public ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<string?>.Success(_secrets.GetValueOrDefault(pointer.Value)));

        public ValueTask<Result<ConfigurationPointer>> WriteAsync(
            string @namespace,
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            var address = $"secrets/{++_next}";
            _secrets[address] = value;
            return ValueTask.FromResult(ConfigurationPointer.Parse("bitwarden", address));
        }

        public ValueTask<Result<ConfigurationRemoval>> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
        {
            // Absent is success, so a retried move is not a failure.
            RemovedPointer = pointer.Value;
            if (!RetainOnRemoval)
                _secrets.Remove(pointer.Value);
            return ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval
            {
                RemoteValueRemains = _secrets.ContainsKey(pointer.Value),
            }));
        }
    }
}
