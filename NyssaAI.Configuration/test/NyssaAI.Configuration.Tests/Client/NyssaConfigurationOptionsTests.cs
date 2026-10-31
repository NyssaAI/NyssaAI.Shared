using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.Tests.Client;

/// <summary>
/// The startup options: what a fresh instance already answers, and what the three registration
/// shapes do with one. Every value is settled by the constructor, so there is no phase between
/// construction and use in which a value is undecided.
/// </summary>
public sealed class NyssaConfigurationOptionsTests
{
    [Fact]
    public void Constructor_TakesTheApplicationNameFromTheEntryAssembly()
    {
        var options = new NyssaConfigurationOptions();

        Assert.False(string.IsNullOrWhiteSpace(options.ApplicationName));
    }

    [Fact]
    public void Constructor_DefaultsTheConfigDirectoryBeneathTheUserManifestFolder()
    {
        var options = new NyssaConfigurationOptions();

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nyssa-ai", options.ApplicationName),
            options.ConfigDirectory);
    }

    [Fact]
    public void Constructor_DefaultsTheManifestToTheUsersOwn()
    {
        var options = new NyssaConfigurationOptions();

        Assert.Equal(BootstrapOptions.DefaultConfigManifest, options.Bootstrap.ConfigManifest);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nyssa-ai", "config-manifest.json"),
            options.Bootstrap.ConfigManifest);
    }

    [Fact]
    public void Constructor_DefaultsToAPositiveLockTimeoutAndNoKeyVault()
    {
        var options = new NyssaConfigurationOptions();

        Assert.True(options.Bootstrap.LockTimeout > TimeSpan.Zero);
        Assert.Null(options.Bootstrap.KeyVault);
    }

    [Fact]
    public void ManifestPathFor_IsTheConventionalPathBeneathAHome()
    {
        Assert.Equal(
            Path.Combine("home", ".nyssa-ai", "config-manifest.json"),
            BootstrapOptions.ManifestPathFor("home"));
    }

    [Fact]
    public void AddNyssaConfiguration_WithNothing_Registers()
    {
        var services = new ServiceCollection();

        services.AddNyssaConfiguration();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfiguration));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigurationExtensions));
    }

    [Fact]
    public void AddNyssaConfiguration_WithAnInstance_Registers()
    {
        var supplied = new NyssaConfigurationOptions { ApplicationName = "tool" };
        supplied.Bootstrap.KeyVault = new VaultOptions
        {
            Name = "contoso",
            Kind = "contoso",
            Settings = { ["tenant"] = "prod" },
            Secrets = { ["clientSecret"] = "fictional" },
        };

        var services = new ServiceCollection().AddNyssaConfiguration(supplied);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfiguration));
        Assert.Equal("prod", supplied.Bootstrap.KeyVault!.Settings["tenant"]);
        Assert.Equal("fictional", supplied.Bootstrap.KeyVault!.Secrets["clientSecret"]);
    }

    [Fact]
    public void AddNyssaConfiguration_WithADelegate_EditsAnAlreadyDefaultedInstance()
    {
        var services = new ServiceCollection();
        string? sawDefaultManifest = null;

        services.AddNyssaConfiguration(options =>
        {
            sawDefaultManifest = options.Bootstrap.ConfigManifest;
            options.ApplicationName = "tool";
        });

        Assert.Equal(BootstrapOptions.DefaultConfigManifest, sawDefaultManifest);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfiguration));
    }

    [Fact]
    public void AddNyssaConfiguration_CalledTwice_KeepsTheFirstRegistration()
    {
        var services = new ServiceCollection();

        services.AddNyssaConfiguration(options => options.ApplicationName = "first");
        var count = services.Count;
        services.AddNyssaConfiguration(options => options.ApplicationName = "second");

        Assert.Equal(count, services.Count);
    }
}
