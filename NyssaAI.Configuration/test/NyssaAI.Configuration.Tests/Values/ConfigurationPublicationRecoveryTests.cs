using System.Security.Cryptography;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Values;

public sealed class ConfigurationPublicationRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_PublicationFails_ReusesConfirmedLocalOutput(bool move)
    {
        using var directory = new TemporaryDirectory();
        var documents = new FailingPublicationFactory(new ConfigurationDocumentFactoryLocalFile(directory.Path));
        var key = RandomNumberGenerator.GetBytes(32);
        var recovery = new ConfigurationRecovery(key, "app", directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(key).ThrowIfError();
        var store = ConfigurationStore.Create(documents, [new ConfigurationProviderLocalEncryption(cipher)], [], recovery: recovery).ThrowIfError();
        var token = TestContext.Current.CancellationToken;
        (await store.SetAsync("mail", "A", "original", cancellationToken: token)).ThrowIfError();
        documents.FailNext = true;
        ConfigurationRecoveryHandle? handle;
        ConfigurationOperationProgress? progress;
        if (move)
        {
            var attempt = await store.MoveAsync("mail", "A", "aesLocal", token);
            Assert.True(attempt.IsFailure);
            handle = attempt.RecoveryHandle;
            progress = attempt.Progress;
        }
        else
        {
            var attempt = await store.SetAsAsync("mail", null, new { A = "updated", B = "new" }, cancellationToken: token);
            Assert.True(attempt.IsFailure);
            handle = attempt.RecoveryHandle;
            progress = attempt.Progress;
        }
        Assert.NotNull(handle);
        Assert.Equal(ConfigurationPublicationState.Failed, progress!.Publication);
        Assert.Equal("original", (await store.GetAsync("mail", "A", token)).ThrowIfError());
        var result = (await store.RecoverAsync(handle, token)).ThrowIfError();
        Assert.Equal(ConfigurationRecoveryStatus.Completed, result.Status);
        Assert.Equal(move ? "original" : "updated", (await store.GetAsync("mail", "A", token)).ThrowIfError());
        if (!move) Assert.Equal("new", (await store.GetAsync("mail", "B", token)).ThrowIfError());
    }

    private sealed class FailingPublicationFactory : IConfigurationDocumentFactory
    {
        private readonly IConfigurationDocumentFactory _inner;
        public bool FailNext { get; set; }
        public FailingPublicationFactory(IConfigurationDocumentFactory inner) => _inner = inner;
        public Result<IConfigurationDocument> Create(string @namespace)
        {
            var opened = _inner.Create(@namespace);
            return opened.IsFailure ? Result<IConfigurationDocument>.Failure(opened.Errors)
                : Result<IConfigurationDocument>.Success(new FailingPublication(this, opened.Value));
        }
        private sealed class FailingPublication : IConfigurationDocument
        {
            private readonly FailingPublicationFactory _owner;
            private readonly IConfigurationDocument _inner;
            public FailingPublication(FailingPublicationFactory owner, IConfigurationDocument inner) { _owner = owner; _inner = inner; }
            public ValueTask<Result<IReadOnlyDictionary<string, ConfigurationValue>>> ReadAsync(CancellationToken cancellationToken = default) =>
                _inner.ReadAsync(cancellationToken);
            public ValueTask<Result> ModifyAsync(Func<IDictionary<string, ConfigurationValue>, ValueTask<Result>> change, CancellationToken cancellationToken = default) =>
                _inner.ModifyAsync(async entries =>
                {
                    var changed = await change(entries);
                    if (changed.IsSuccess && _owner.FailNext)
                    {
                        _owner.FailNext = false;
                        return Result.Failure(Error.FromMessage("Publication failed", ErrorCategory.Unavailable));
                    }
                    return changed;
                }, cancellationToken);
        }
    }
}
