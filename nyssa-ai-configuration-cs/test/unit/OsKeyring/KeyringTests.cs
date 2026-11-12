using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.OsKeyring;

public sealed class KeyringTests
{
    [Theory]
    [InlineData("nyssa-ai-configuration._manifest")]
    [InlineData("_manifest.key")]
    [InlineData("_manifest")]
    [InlineData("nyssa-manifest-key")]
    [InlineData("nyssa-manifest-key.key")]
    public void KeyringSystemdCreds_ManifestAccount_ReadsDedicatedManifestCredentialFiles(string fileName)
    {
        using var credsDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(credsDir.Path, fileName), "manifest-secret-material\n");

        var keyring = new KeyringSystemdCreds(credentialsDirectory: credsDir.Path);
        var result = keyring.Get("nyssa-ai-configuration", "_manifest");

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal("manifest-secret-material", result.Value);

        // An application account lookup MUST NOT steal the single manifest credential file via wildcard fallback
        var appResult = keyring.Get("nyssa-ai-configuration", "SomeApp");
        Assert.True(appResult.IsFailure);
        Assert.Equal(ErrorCategory.NotFound, appResult.Errors[0].Category);
    }

    [Theory]
    [InlineData("nyssa-ai-configuration.SomeApp")]
    [InlineData("nyssa-key")]
    [InlineData("master-key")]
    [InlineData("nyssa-ai-configuration")]
    public void KeyringSystemdCreds_ManifestAccount_NeverFallsBackToAppOrSingleFileCredentials(string appFileName)
    {
        using var credsDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(credsDir.Path, appFileName), "app-secret-material\n");

        var keyring = new KeyringSystemdCreds(credentialsDirectory: credsDir.Path);
        var manifestResult = keyring.Get("nyssa-ai-configuration", "_manifest");

        Assert.True(manifestResult.IsFailure);
        Assert.Equal(ErrorCategory.NotFound, manifestResult.Errors[0].Category);

        var appResult = keyring.Get("nyssa-ai-configuration", "SomeApp");
        Assert.True(appResult.IsSuccess, appResult.ToString());
        Assert.Equal("app-secret-material", appResult.Value);
    }

    [Fact]
    public void KeyringSystemdCreds_AppSingleFileFallback_IgnoresManifestCredentialsWhenBothExist()
    {
        using var credsDir = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(credsDir.Path, "nyssa-ai-configuration._manifest"), "manifest-only-key");
        File.WriteAllText(Path.Combine(credsDir.Path, "custom-mounted-app-cred"), "single-app-key");

        var keyring = new KeyringSystemdCreds(credentialsDirectory: credsDir.Path);

        Assert.Equal("manifest-only-key", keyring.Get("nyssa-ai-configuration", "_manifest").Value);
        Assert.Equal("single-app-key", keyring.Get("nyssa-ai-configuration", "SomeApp").Value);
    }
}
