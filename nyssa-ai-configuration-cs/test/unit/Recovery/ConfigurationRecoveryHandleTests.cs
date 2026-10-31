using System.Security.Cryptography;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Configuration.Tests.Support;

namespace NyssaAI.Configuration.Tests.Recovery;

public sealed class ConfigurationRecoveryHandleTests
{
    [Theory]
    [InlineData("tampered")]
    [InlineData("root")]
    [InlineData("application")]
    [InlineData("key")]
    [InlineData("version")]
    public void Open_ChangedProtectionContext_RejectsHandle(string change)
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        var protector = new ConfigurationRecovery(key, "app", directory.Path);
        var state = new ConfigurationRecoveryState
        {
            OperationId = Guid.NewGuid(), Namespace = "mail", Kind = ConfigurationOperationKind.Replace,
            Original = [], Proposed = [], Steps = [], Version = change == "version" ? 2 : 1,
        };
        var handle = protector.Seal(state).ThrowIfError();
        var other = new ConfigurationRecovery(change == "key" ? RandomNumberGenerator.GetBytes(32) : key,
            change == "application" ? "other" : "app", change == "root" ? Path.Combine(directory.Path, "other") : directory.Path);
        if (change == "tampered")
        {
            var token = handle.Export();
            var index = token.Length / 2;
            token = token[..index] + (token[index] == 'A' ? 'B' : 'A') + token[(index + 1)..];
            handle = ConfigurationRecoveryHandle.Import(token);
        }
        Assert.True(other.Open(handle).IsFailure);
    }

    [Fact]
    public void Open_OrdinaryConfigurationCipher_CannotForgeHandle()
    {
        using var directory = new TemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        var ordinary = ConfigurationCipherAesGcm.Create(key).ThrowIfError();
        var root = OperatingSystem.IsWindows() ? directory.Path.ToUpperInvariant() : directory.Path;
        var forged = ordinary.Seal("""{"Version":1,"Namespace":"mail"}""", "app", root).ThrowIfError();
        var protector = new ConfigurationRecovery(key, "app", directory.Path);
        Assert.True(protector.Open(ConfigurationRecoveryHandle.Import(forged)).IsFailure);
    }
}
