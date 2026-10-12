using NyssaAI.Configuration.Bootstrap;
using NyssaAI.Configuration.Tests.Support;

namespace NyssaAI.Configuration.Tests.Bootstrap;

public sealed class ConfigurationManifestWriterTests
{
    [Fact]
    public void SetConfigurationDirectory_WhenNoManifestExists_CreatesOneWithTheSection()
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
        Assert.Contains("\"version\": 1", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SetConfigurationDirectory_KeepsTheSectionsKeyVaultAndEveryOtherSection()
    {
        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "version": 1,
              "applications": {
                "tool": { "lockTimeout": "00:00:02", "keyVault": { "name": "hsm", "kind": "fabrikam", "secrets": { "clientSecret": "fictional" } } },
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
        Assert.Equal("fictional", tool.Value.KeyVault!.Secrets!["clientSecret"]);
        var other = ConfigurationManifestReader.ReadApplication(path, "other");
        Assert.True(other.IsSuccess, other.ToString());
        Assert.EndsWith("other", other.Value.ConfigurationDirectory, StringComparison.Ordinal);
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
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Privacy is expressed as POSIX mode bits here.");
            return;
        }

        using var home = new TemporaryDirectory();
        var path = ConfigurationManifestReader.DefaultPathFor(home.Path);

        ConfigurationManifestWriter.SetConfigurationDirectory(path, "tool", home.Path);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }
}
