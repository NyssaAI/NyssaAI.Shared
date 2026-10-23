using NyssaAI.Configuration.Manifest;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Manifest;

public sealed class ConfigurationManifestReaderTests
{
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
        Assert.Null(read.Value.KeyVault);
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
    public void ReadApplication_CarriesTheKeyVaultSettingsSecretsAndTheLockTimeoutThrough()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, """
            {
              // comments are allowed: an operator will annotate this file
              "version": 1,
              "applications": {
                "tool": {
                  "configurationDirectory": "tool",
                  "lockTimeout": "00:00:02",
                  "keyVault": { "name": "hsm", "kind": "fabrikam", "endpoint": "https://vault.example", "credential": "vault/token", "settings": { "tenant": "prod" }, },
                },
              },
            }
            """);

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal(TimeSpan.FromSeconds(2), read.Value.LockTimeout);
        var hsm = read.Value.KeyVault!;
        Assert.Equal("hsm", hsm.Name);
        Assert.Equal("fabrikam", hsm.Kind);
        Assert.Equal("https://vault.example", hsm.Endpoint);
        Assert.Equal("vault/token", hsm.Credential);
        Assert.Equal(new Dictionary<string, string> { ["tenant"] = "prod" }, hsm.Settings);
        // An absent settings or secrets key is an empty map, never null
        Assert.Empty(hsm.Secrets!);
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
        var path = Write(home.Path, """{ "version": 1, "applications": { "tool": { "configurationDirectory": "tool", "keyVault": { "name": "x", "endpoint": "https://vault.example" } } } }""");

        var read = ConfigurationManifestReader.Read(path);

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(read.Errors).Category);
    }

    [Fact]
    public void ReadApplication_WithSecretsInAPrivateFile_Succeeds()
    {
        using var home = new TemporaryDirectory();
        var path = Write(home.Path, SecretsManifest);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal("fictional", read.Value.KeyVault!.Secrets!["clientSecret"]);
    }

    [Theory]
    [InlineData(UnixFileMode.GroupRead, "group")]
    [InlineData(UnixFileMode.OtherRead, "other")]
    public void ReadApplication_WithSecretsInAReadableFile_IsRefusedNamingTheBitAndTheFixAndNeverTheSecret(UnixFileMode exposure, string who)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Exposure is expressed as POSIX mode bits here; Windows ACLs are covered by the FileSecurity tests.");
            return;
        }

        using var home = new TemporaryDirectory();
        var path = Write(home.Path, SecretsManifest);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | exposure);

        var read = ConfigurationManifestReader.ReadApplication(path, "tool");

        Assert.True(read.IsFailure);
        Assert.Equal(ErrorCategory.Forbidden, Assert.Single(read.Errors).Category);
        Assert.Contains(who, read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains($"chmod 600 '{path}'", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional", read.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional", read.Errors[0].UserMessage, StringComparison.Ordinal);
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
        var path = Write(home.Path, SecretsManifest);
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
        Assert.DoesNotContain("fictional", read.Errors[0].UserMessage, StringComparison.Ordinal);
    }

    private const string SecretsManifest = """
        {
          "version": 1,
          "applications": {
            "tool": {
              "configurationDirectory": "tool",
              "keyVault": { "name": "hsm", "kind": "fabrikam", "secrets": { "clientSecret": "fictional" } }
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
}
