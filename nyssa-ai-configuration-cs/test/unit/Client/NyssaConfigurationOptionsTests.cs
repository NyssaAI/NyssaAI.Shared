using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.SecretInput;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Results;

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
    public void Constructor_DefaultsToAPositiveLockTimeoutAndNoKeyVaults()
    {
        var options = new NyssaConfigurationOptions();

        Assert.True(options.Bootstrap.LockTimeout > TimeSpan.Zero);
        Assert.Null(options.Bootstrap.KeyVault);
        Assert.Empty(options.Bootstrap.KeyVaults);
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
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigurationReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigurationWriter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigurationMaintainer));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigurationOAuthWriter));
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

    [Fact]
    public void AddNyssaConfiguration_WithDuplicateProgrammaticVaultNames_ThrowsAtResolution()
    {
        using var home = new TemporaryDirectory();
        var keyring = new InMemoryKeyring();
        var services = new ServiceCollection();
        services.AddSingleton<IKeyring>(keyring);
        services.AddNyssaConfiguration(options =>
        {
            options.ApplicationName = "tool";
            options.ConfigDirectory = Path.Combine(home.Path, "tool");
            options.Bootstrap.ConfigManifest = BootstrapOptions.ManifestPathFor(home.Path);
            options.Bootstrap.KeyVault = new VaultOptions { Name = "bws-prod", Kind = "bitwarden-secrets", Secrets = { ["accessToken"] = "tok1" } };
            options.Bootstrap.KeyVaults.Add(new VaultOptions { Name = "bws-prod", Kind = "bitwarden-secrets", Secrets = { ["accessToken"] = "tok2" } });
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IConfiguration>());
        Assert.Contains("bws-prod", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetAndRemoveManifestKeyVault_SealsSecretsWithManifestKeyAndSupportsPreserveExistingSecrets()
    {
        using var home = new TemporaryDirectory();
        var manifestPath = BootstrapOptions.ManifestPathFor(home.Path);
        var keyring = new InMemoryKeyring();

        ConfigurationServiceCollectionExtensions.SetManifestKeyVault(
            manifestPath,
            new VaultOptions
            {
                Name = "bws-corporate",
                Kind = "bitwarden-secrets",
                CacheTTLSeconds = 60,
                Settings = { ["organizationId"] = "org-1", ["projectId"] = "proj-1" },
                Secrets = { ["accessToken"] = "bws-secret-token-alpha" },
            },
            keyring);

        Assert.True(keyring.Stored.ContainsKey(
            $"{ConfigurationServiceCollectionExtensions.KeyringService}/{ConfigurationServiceCollectionExtensions.ManifestKeyAccount}"));
        var rawJson = File.ReadAllText(manifestPath);
        Assert.DoesNotContain("bws-secret-token-alpha", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"encrypted\": true", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"provider\": \"aesLocal\"", rawJson, StringComparison.Ordinal);

        // Updating metadata with preserveExistingSecrets: true does not require keyring access when Secrets is empty
        var unreachableKeyring = new InMemoryKeyring { FailAll = true };
        ConfigurationServiceCollectionExtensions.SetManifestKeyVault(
            manifestPath,
            new VaultOptions
            {
                Name = "bws-corporate",
                Kind = "bitwarden-secrets",
                CacheTTLSeconds = 300,
                Settings = { ["organizationId"] = "org-1", ["projectId"] = "proj-updated" },
            },
            unreachableKeyring,
            preserveExistingSecrets: true);

        var updatedJson = File.ReadAllText(manifestPath);
        Assert.Contains("\"proj-updated\"", updatedJson, StringComparison.Ordinal);
        Assert.Contains("\"encrypted\": true", updatedJson, StringComparison.Ordinal);

        Assert.True(ConfigurationServiceCollectionExtensions.RemoveManifestKeyVault(manifestPath, "bws-corporate"));
        Assert.False(ConfigurationServiceCollectionExtensions.RemoveManifestKeyVault(manifestPath, "bws-corporate"));
    }

    [Theory]
    [InlineData("aesLocal", "Encrypted Locally (AES-256-GCM)")]
    [InlineData("literal", "Free Text (Unencrypted Plaintext)")]
    [InlineData("bws-corporate", "Key Vault (Bitwarden Secrets Manager) [bws-corporate]")]
    [InlineData("bws-prod", "Key Vault (Bitwarden Secrets Manager) [bws-prod]")]
    [InlineData("bws-staging", "Key Vault (Bitwarden Secrets Manager) [bws-staging]")]
    [InlineData("bitwarden-prod", "Key Vault (Bitwarden Secrets Manager) [bitwarden-prod]")]
    public void FormatProviderLabel_IncludesDistinctVaultNamesForBitwardenPrefixes(string providerName, string expectedLabel)
    {
        Assert.Equal(expectedLabel, CliSecretInputProvider.FormatProviderLabel(providerName));
        Assert.Equal(expectedLabel, LoopbackSecretInputProvider.FormatProviderLabel(providerName));
    }

    private sealed class InMemoryKeyring : IKeyring
    {
        public Dictionary<string, string> Stored { get; } = new(StringComparer.Ordinal);

        public bool FailAll { get; init; }

        public Result<string?> Get(string service, string key) =>
            FailAll
                ? Result<string?>.Failure(Error.FromMessages("Unavailable", "keyring unavailable", ErrorCategory.Unavailable))
                : Result<string?>.Success(Stored.GetValueOrDefault($"{service}/{key}"));

        public Result Set(string service, string key, string secret)
        {
            if (FailAll)
                return Result.Failure(Error.FromMessages("Unavailable", "keyring unavailable", ErrorCategory.Unavailable));
            Stored[$"{service}/{key}"] = secret;
            return Result.Success();
        }

        public Result Delete(string service, string key)
        {
            if (FailAll)
                return Result.Failure(Error.FromMessages("Unavailable", "keyring unavailable", ErrorCategory.Unavailable));
            Stored.Remove($"{service}/{key}");
            return Result.Success();
        }
    }
}
