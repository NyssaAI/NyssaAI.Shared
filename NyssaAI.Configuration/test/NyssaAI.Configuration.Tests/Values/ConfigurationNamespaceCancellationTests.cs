using System.Text.Json;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Values;

public sealed class ConfigurationNamespaceCancellationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task NamespaceOperation_CanceledDuringProviderCall_ReturnsCanceled(bool writing, bool providerThrows, bool merge)
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new CancelingProvider(cancellation, providerThrows);
        var store = ConfigurationStore.Create(new ConfigurationDocumentFactoryLocalFile(directory.Path), [provider]).ThrowIfError();
        (await store.SetAsync("settings", "First", "original", provider.Name, cancellation.Token)).ThrowIfError();
        (await store.SetAsync("settings", "Second", "original", cancellationToken: cancellation.Token)).ThrowIfError();
        (await store.SetAsync("settings", "Omitted", "preserved on failure", cancellationToken: cancellation.Token)).ThrowIfError();
        var before = File.ReadAllText(Path.Combine(directory.Path, "settings.json"));
        provider.Armed = true;

        if (writing)
        {
            using var dto = JsonDocument.Parse("""{"First":"updated","Second":"updated"}""");
            var result = merge
                ? await store.MergeSettingsAsync("settings", dto.RootElement, cancellation.Token)
                : await store.SetAsAsync("settings", null, dto.RootElement, cancellationToken: cancellation.Token);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
        }
        else
        {
            var result = await store.GetAsAsync<JsonElement>("settings", null, cancellation.Token);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCategory.Canceled, Assert.Single(result.Errors).Category);
        }

        Assert.Equal(before, File.ReadAllText(Path.Combine(directory.Path, "settings.json")));
    }

    private sealed class CancelingProvider : IConfigurationProvider
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _throws;

        public CancelingProvider(CancellationTokenSource cancellation, bool throws)
        {
            _cancellation = cancellation;
            _throws = throws;
        }

        public string Name => "canceling";
        public bool Armed { get; set; }

        public ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default)
        {
            CancelIfArmed(cancellationToken);
            return ValueTask.FromResult(Result<string>.Success(entry.Value));
        }

        public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default)
        {
            CancelIfArmed(cancellationToken);
            return ValueTask.FromResult(Result<ConfigurationValue>.Success(new ConfigurationValue { Value = value, Provider = Name }));
        }

        public ValueTask<Result<ConfigurationRemoval>> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false }));

        private void CancelIfArmed(CancellationToken cancellationToken)
        {
            if (!Armed)
                return;
            _cancellation.Cancel();
            if (_throws)
                cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
