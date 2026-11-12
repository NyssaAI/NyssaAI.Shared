using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Manifest;
using NyssaAI.Configuration.Tests.Support;

namespace NyssaAI.Configuration.Tests.Manifest;

public sealed class ConfigurationManifestWriterTests
{
    private static readonly byte[] TestManifestKey =
        Enumerable.Range(11, 32).Select(index => (byte)index).ToArray();

    [Fact]
    public void SetConfigurationDirectory_WhenNoManifestExists_CreatesCompactManifestOmittingEmptyKeyVaults()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);
        var directory = Path.Combine(home.Path, "tool-config");

        var written = ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", directory);

        Assert.True(written.IsSuccess, written.ToString());
        Assert.Equal(directory, written.Value);
        var read = ConfigurationManifestReader.ReadApplication(path, "tool");
        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal(directory, read.Value.ConfigurationDirectory);
        var rawJson = File.ReadAllText(path);
        Assert.Contains("\"version\": 1", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"keyVaults\"", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SetConfigurationDirectory_KeepsRootKeyVaultsEncryptedSecretsAndEveryOtherSection()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sealedSecret = SealSecret("hsm", "clientSecret", "fictional");
        File.WriteAllText(path, $$"""
            {
              "version": 1,
              "keyVaults": {
                "hsm": {
                  "kind": "fabrikam",
                  "secrets": {
                    "clientSecret": {
                      "value": "{{sealedSecret}}",
                      "provider": "aesLocal",
                      "encrypted": true
                    }
                  }
                }
              },
              "applications": {
                "tool": { "lockTimeout": "00:00:02" },
                "other": { "configurationDirectory": "other" }
              }
            }
            """);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var directory = Path.Combine(home.Path, "tool-config");

        var written = ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", directory);

        Assert.True(written.IsSuccess, written.ToString());
        var tool = ConfigurationManifestReader.ReadApplication(path, "tool");
        Assert.True(tool.IsSuccess, tool.ToString());
        Assert.Equal(directory, tool.Value.ConfigurationDirectory);
        Assert.Equal(TimeSpan.FromSeconds(2), tool.Value.LockTimeout);
        Assert.Equal(sealedSecret, tool.Value.KeyVaults["hsm"].Secrets!["clientSecret"].Value);
        var other = ConfigurationManifestReader.ReadApplication(path, "other");
        Assert.True(other.IsSuccess, other.ToString());
        Assert.EndsWith("other", other.Value.ConfigurationDirectory, StringComparison.Ordinal);

        var rawJson = File.ReadAllText(path);
        Assert.DoesNotContain("\"name\":", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"settings\":", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SetConfigurationDirectory_CalledAgain_ReplacesTheDirectory()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);
        var first = Path.Combine(home.Path, "first");
        var second = Path.Combine(home.Path, "second");

        ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", first);
        var written = ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", second);

        Assert.True(written.IsSuccess, written.ToString());
        Assert.Equal(second, ConfigurationManifestReader.ReadApplication(path, "tool").Value.ConfigurationDirectory);
    }

    [Fact]
    public void SetConfigurationDirectory_LeavesThePublishedFilePrivate()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);

        ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", home.Path);

        var privateFile = NyssaAI.Configuration.FileSecurity.DirectorySafety.VerifyPrivateFile(path);
        Assert.True(privateFile.IsSuccess, privateFile.ToString());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void SetKeyVault_PreserveExistingSecretsAndRemoveKeyVault_MaintainsCompactManifestAndSiblings()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);
        ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", Path.Combine(home.Path, "tool"));

        var sealedToken = SealSecret("bws-corporate", "accessToken", "corp-token-1");
        var created = ConfigurationManifestWriter.SetKeyVault(
            path,
            new VaultManifestEntry
            {
                Name = "bws-corporate",
                Kind = "bitwarden-secrets",
                CacheTTLSeconds = 60,
                Settings = new Dictionary<string, string> { ["projectId"] = "proj-1" },
                Secrets = new Dictionary<string, ManifestSecretEnvelope>
                {
                    ["accessToken"] = new ManifestSecretEnvelope
                    {
                        Value = sealedToken,
                        Provider = "aesLocal",
                        Encrypted = true,
                    },
                },
            });
        Assert.True(created.IsSuccess, created.ToString());

        // Update settings/TTL with preserveExistingSecrets: true and no new secrets
        var updated = ConfigurationManifestWriter.SetKeyVault(
            path,
            new VaultManifestEntry
            {
                Name = "bws-corporate",
                Kind = "bitwarden-secrets",
                CacheTTLSeconds = 120,
                Settings = new Dictionary<string, string> { ["projectId"] = "proj-2" },
            },
            preserveExistingSecrets: true);
        Assert.True(updated.IsSuccess, updated.ToString());

        var afterPreserve = ConfigurationManifestReader.Read(path).Value!;
        Assert.Equal(120, afterPreserve.KeyVaults!["bws-corporate"].CacheTTLSeconds);
        Assert.Equal("proj-2", afterPreserve.KeyVaults["bws-corporate"].Settings!["projectId"]);
        Assert.Equal(sealedToken, afterPreserve.KeyVaults["bws-corporate"].Secrets!["accessToken"].Value);

        // Remove vault -> returns true and omits empty "keyVaults" on disk while keeping "applications"
        var removed = ConfigurationManifestWriter.RemoveKeyVault(path, "bws-corporate");
        Assert.True(removed.IsSuccess && removed.Value);
        Assert.False(ConfigurationManifestWriter.RemoveKeyVault(path, "bws-corporate").Value);

        var finalJson = File.ReadAllText(path);
        Assert.DoesNotContain("\"keyVaults\"", finalJson, StringComparison.Ordinal);
        Assert.Contains("\"tool\"", finalJson, StringComparison.Ordinal);
    }

    private static string SealSecret(string vaultName, string secretKey, string plaintext)
    {
        var cipher = ConfigurationCipherAesGcm.Create(TestManifestKey).Value;
        return cipher.Seal(plaintext, $"keyVaults/{vaultName}", secretKey).Value;
    }
}
