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
        var cipher = new CountingCipher(ConfigurationCipherAesGcm.Create(key).ThrowIfError());
        var store = ConfigurationStore.Create(documents, [new ConfigurationProviderLocalEncryption(cipher)], [], recovery: recovery).ThrowIfError();
        var token = TestContext.Current.CancellationToken;
        (await store.SetAsync("mail", "A", "original", move ? null : "aesLocal", token)).ThrowIfError();
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
        var sealsBeforeRecovery = cipher.Seals;
        Assert.Equal(move ? 1 : 2, sealsBeforeRecovery);
        var result = (await store.RecoverAsync(handle, token)).ThrowIfError();
        Assert.Equal(ConfigurationRecoveryStatus.Completed, result.Status);
        Assert.Equal(sealsBeforeRecovery, cipher.Seals);
        var published = (await documents.Create("mail").ThrowIfError().ReadAsync(token)).ThrowIfError();
        Assert.Equal(documents.FailedEntries!["A"], published["A"]);
        Assert.Equal(move ? "original" : "updated", (await store.GetAsync("mail", "A", token)).ThrowIfError());
        if (!move) Assert.Equal("new", (await store.GetAsync("mail", "B", token)).ThrowIfError());
    }

    private sealed class CountingCipher(IConfigurationCipher inner) : IConfigurationCipher
    {
        public int Seals { get; private set; }

        public Result<string> Seal(string value, string @namespace, string key)
        {
            Seals++;
            return inner.Seal(value, @namespace, key);
        }

        public Result<string> Open(string value, string @namespace, string key) => inner.Open(value, @namespace, key);
    }
}
