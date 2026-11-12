using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Manifest;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Manifest;

public sealed class ConfigurationManifestReaderTests
{
    private static readonly byte[] TestManifestKey =
        Enumerable.Range(1, 32).Select(index => (byte)index).ToArray();

    [Fact]
    public void DefaultPathFor_IsTheUsersConfigManifest()
    {
        Assert.Equal(
            Path.Combine("home", ".nyssa-ai", "config-manifest.json"),
            ConfigurationManifestReader.DefaultPathFor("home"));
    }

    [Fact]
    public void Read_WhenNoManifestExists_YieldsNull()
    {
        using var home = new TemporaryDirectory();

        var read = ConfigurationManifestReader.Read(ConfigurationManifestReader.DefaultPathFor(home.Path));

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Null(read.Value);
    }

    [Fact]
    public void ReadApplication_WhenNoManifestExists_IsNotFoundNamingThePathAndTheTool()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.NotFound, Assert.Single(read.Errors).Category);
        Assert.Equal("The configuration could not be found.", read.Errors[0].UserMessage);
        Assert.Contains(path, read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains("'tool'", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains("init", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadApplication_WhenTheSectionIsAbsent_IsNotFoundNamingTheTool()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """{ "version": 1, "applications": { "other": { "configurationDirectory": "other" } } }""");

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.NotFound, Assert.Single(read.Errors).Category);
        Assert.Contains("'tool'", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadApplication_WhenTheSectionNamesNoDirectory_IsNotFound()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """{ "version": 1, "applications": { "tool": { "lockTimeout": "00:00:01" } } }""");

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.NotFound, Assert.Single(read.Errors).Category);
        Assert.Contains("configurationDirectory", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadApplication_ResolvesARelativeDirectoryBesideTheManifest()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """{ "version": 1, "applications": { "tool": { "configurationDirectory": "tool" } } }""");

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal(Path.Combine(Path.GetDirectoryName(path)!, "tool"), read.Value.ConfigurationDirectory);
        Assert.Null(read.Value.LockTimeout);
        Assert.Empty(read.Value.KeyVaults);
    }

    [Fact]
    public void ReadApplication_KeepsAnAbsoluteDirectory()
    {
        using var home = new TemporaryDirectory();
        var absolute = Path.Combine(home.Path, "elsewhere");
        var path = Write(home.Path, $$"""{ "version": 1, "applications": { "tool": { "configurationDirectory": {{System.Text.Json.JsonSerializer.Serialize(absolute)}} } } }""");

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal(absolute, read.Value.ConfigurationDirectory);
    }

    [Fact]
    public void ReadApplication_CarriesRootKeyVaultsSettingsSecretsAndTheLockTimeoutThrough()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """
            {
              // comments are allowed: an operator will annotate this file
              "version": 1,
              "keyVaults": {
                "hsm": {
                  "kind": "fabrikam",
                  "endpoint": "https://vault.example",
                  "credential": "oauth://vault/token",
                  "cacheTTLSeconds": 120,
                  "settings": { "tenant": "prod" },
                },
              },
              "applications": {
                "tool": {
                  "configurationDirectory": "tool",
                  "lockTimeout": "00:00:02",
                },
              },
            }
            """);

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal(TimeSpan.FromSeconds(2), read.Value.LockTimeout);
        var hsm = read.Value.KeyVaults["hsm"];
        Assert.Equal("hsm", hsm.Name);
        Assert.Equal("fabrikam", hsm.Kind);
        Assert.Equal("https://vault.example", hsm.Endpoint);
        Assert.Equal("vault/token", hsm.Credential);
        Assert.Equal(120, hsm.CacheTTLSeconds);
        Assert.Equal(new Dictionary<string, string> { ["tenant"] = "prod" }, hsm.Settings);
        Assert.Empty(hsm.Secrets!);
    }

    [Fact]
    public void Read_LegacyPerApplicationKeyVault_IsAValidationFailureNamingKeyVaults()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """
            {
              "version": 1,
              "applications": {
                "tool": {
                  "configurationDirectory": "tool",
                  "keyVault": { "name": "hsm", "kind": "fabrikam" }
                }
              }
            }
            """);

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
        Assert.Contains("applications.tool.keyVault", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains("keyVaults", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AVaultWithNegativeCacheTTLSeconds_IsAValidationFailure()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """
            {
              "version": 1,
              "keyVaults": {
                "x": { "kind": "fabrikam", "cacheTTLSeconds": -1 }
              },
              "applications": {
                "tool": { "configurationDirectory": "tool" }
              }
            }
            """);

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
        Assert.Contains("cacheTTLSeconds", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadApplication_WithLockTimeoutMs_ParsesMillisecondsCorrectly()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """{ "version": 1, "applications": { "tool": { "configurationDirectory": "tool", "lockTimeoutMs": 5000 } } }""");

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal(TimeSpan.FromSeconds(5), read.Value.LockTimeout);
    }

    [Fact]
    public void ReadApplication_WithANonPositiveLockTimeout_IsAValidationFailure()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """{ "version": 1, "applications": { "tool": { "configurationDirectory": "tool", "lockTimeout": "00:00:00" } } }""");

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
    }

    [Fact]
    public void Read_AMalformedManifest_IsAValidationFailureNamingThePath()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, "{ \"applications\": [ ] ");

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
        Assert.Contains(path, read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AnUnsupportedVersion_IsRefusedRatherThanGuessedAt()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """{ "version": 2, "applications": { } }""");

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
        Assert.Contains("version 1", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AVaultWithoutAKind_IsAValidationFailure()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """
            {
              "version": 1,
              "keyVaults": {
                "x": { "endpoint": "https://vault.example" }
              },
              "applications": {
                "tool": { "configurationDirectory": "tool" }
              }
            }
            """);

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
    }

    [Theory]
    [InlineData("aesLocal")]
    [InlineData("literal")]
    [InlineData("oauth")]
    [InlineData("bad/name")]
    [InlineData(" bad")]
    public void Read_ReservedOrInvalidVaultName_IsAValidationFailure(string invalidVaultName)
    {
        using var home = new TemporaryDirectory();
        var serializedKey = System.Text.Json.JsonSerializer.Serialize(invalidVaultName);
        var path = Write(home.Path, $$"""
            {
              "version": 1,
              "keyVaults": {
                {{serializedKey}}: { "kind": "fabrikam" }
              },
              "applications": {
                "tool": { "configurationDirectory": "tool" }
              }
            }
            """);

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
    }

    [Fact]
    public void Read_PlaintextVaultSecret_IsRejectedWithoutEchoingTheSecret()
    {
        using var home = new TemporaryDirectory();
        var path = WritePrivate(home.Path, """
            {
              "version": 1,
              "keyVaults": {
                "bws-corporate": {
                  "kind": "bitwarden-secrets",
                  "secrets": {
                    "accessToken": "super-secret-plaintext-token"
                  }
                }
              },
              "applications": {
                "tool": { "configurationDirectory": "tool" }
              }
            }
            """);

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
        Assert.Contains(
            "manifest: keyVaults.bws-corporate.secrets.accessToken must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json",
            read.Errors[0].TechnicalMessage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-plaintext-token", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-plaintext-token", read.Errors[0].UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadApplication_WithEncryptedSecretsInAPrivateFile_SucceedsAndRedactsToString()
    {
        using var home = new TemporaryDirectory();
        var sealedCiphertext = SealSecret("hsm", "clientSecret", "fictional");
        var path = WritePrivate(home.Path, EncryptedSecretsManifest(sealedCiphertext));

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        var envelope = read.Value.KeyVaults["hsm"].Secrets!["clientSecret"];
        Assert.Equal(sealedCiphertext, envelope.Value);
        Assert.Equal("aesLocal", envelope.Provider);
        Assert.True(envelope.Encrypted);
        Assert.Contains("[REDACTED]", envelope.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sealedCiphertext, envelope.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UnixFileMode.GroupRead, "group")]
    [InlineData(UnixFileMode.OtherRead, "other")]
    public void Read_WithPlaintextOrEncryptedSecretsInAReadableFile_FailsPrivateGateFirstNamingTheFixAndNeverTheSecret(
        UnixFileMode exposure,
        string who)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Exposure is expressed as POSIX mode bits here; Windows ACLs are covered by the FileSecurity tests.");
            return;
        }

        using var home = new TemporaryDirectory();
        // Even when the secret is malformed plaintext (which would otherwise fail envelope validation),
        // the 0600 private-file gate MUST run first and return Forbidden without exposing the secret.
        var path = Write(home.Path, """
            {
              "version": 1,
              "keyVaults": {
                "hsm": {
                  "kind": "fabrikam",
                  "secrets": { "clientSecret": "fictional-plaintext-exposure" }
                }
              },
              "applications": {
                "tool": { "configurationDirectory": "tool" }
              }
            }
            """);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | exposure);

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Forbidden, Assert.Single(read.Errors).Category);
        Assert.Contains(who, read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains($"chmod 600 '{path}'", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-plaintext-exposure", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-plaintext-exposure", read.Errors[0].UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadApplication_WithSecretsInAnEveryoneReadableFile_IsRefusedNamingTheFixAndNeverTheSecret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows ACL check.");
            return;
        }

        using var home = new TemporaryDirectory();
        var sealedCiphertext = SealSecret("hsm", "clientSecret", "fictional");
        var path = Write(home.Path, EncryptedSecretsManifest(sealedCiphertext));
        var info = new FileInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
            System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Forbidden, Assert.Single(read.Errors).Category);
        Assert.Contains("readable", read.Errors[0].TechnicalMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("icacls", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains(path, read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(sealedCiphertext, read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional", read.Errors[0].UserMessage, StringComparison.Ordinal);
    }

    private static string SealSecret(string vaultName, string secretKey, string plaintext)
    {
        var cipher = ConfigurationCipherAesGcm.Create(TestManifestKey).Value;
        return cipher.Seal(plaintext, $"keyVaults/{vaultName}", secretKey).Value;
    }

    private static string EncryptedSecretsManifest(string sealedCiphertext) =>
        $$"""
        {
          "version": 1,
          "keyVaults": {
            "hsm": {
              "kind": "fabrikam",
              "secrets": {
                "clientSecret": {
                  "value": "{{sealedCiphertext}}",
                  "provider": "aesLocal",
                  "encrypted": true
                }
              }
            }
          },
          "applications": {
            "tool": {
              "configurationDirectory": "tool"
            }
          }
        }
        """;

    private static string Write(string home, string json)
    {
        var path = ConfigurationManifestReader.DefaultPathFor(home);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static string WritePrivate(string home, string json)
    {
        var path = Write(home, json);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }
}
