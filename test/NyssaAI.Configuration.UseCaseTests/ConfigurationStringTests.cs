using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Typed string reads agree with namespace settings across storage providers.</summary>
public sealed class ConfigurationStringTests
{
    [Theory]
    [MemberData(nameof(ConfigurationSettingExamples.StringValues), MemberType = typeof(ConfigurationSettingExamples))]
    public async Task GetAsAsync_NamespaceStrings_ReadsEachProviderAcrossWritePathsAsync(string value, bool useSourceGeneration)
    {
        // Arrange
        using var _fixture = new Workstation { VaultPackage = new FakeVaultFactory() };
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new NamespaceMailSettings
        {
            Host = value, Port = 587, UseTls = true,
            Password = value, ApiKey = value,
        };

        // Act
        _fixture.AddNyssaConfiguration(options => options.Bootstrap.KeyVault = new VaultOptions
        {
            Name = "mailVault", Kind = "fake",
        });
        var configuration = _fixture.Host.GetRequiredService<IConfiguration>();
        // Choose secret destinations before saving the complete settings object.
        await configuration.SetAsync("mail", "Password", value, "aesLocal", cancellationToken);
        await configuration.SetAsync("mail", "api-key", value, "mailVault", cancellationToken);
        await (useSourceGeneration
            ? configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings, cancellationToken)
            : configuration.SetAsAsync("mail", settings, cancellationToken));

        // Read the persisted properties individually through both typed overloads.
        var reader = configuration;
        var plain = await reader.GetAsAsync<string>("mail", "Host", cancellationToken);
        var plainGenerated = await reader.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken);
        var encrypted = await reader.GetAsAsync<string>("mail", "Password", cancellationToken);
        var encryptedGenerated = await reader.GetAsAsync("mail", "Password", AppJsonContext.Default.String, cancellationToken);
        var remote = await reader.GetAsAsync<string>("mail", "api-key", cancellationToken);
        var remoteGenerated = await reader.GetAsAsync("mail", "api-key", AppJsonContext.Default.String, cancellationToken);

        // Alternating keyed and namespace saves must not change how a string is read.
        await (useSourceGeneration
            ? configuration.SetAsAsync("mail", "Host", value, AppJsonContext.Default.String, cancellationToken: cancellationToken)
            : configuration.SetAsAsync("mail", "Host", value, cancellationToken: cancellationToken));
        var keyed = await reader.GetAsAsync<string>("mail", "Host", cancellationToken);
        var keyedGenerated = await reader.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken);
        var assembled = await reader.GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken);
        await configuration.SetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, settings, cancellationToken);
        var rewritten = await reader.GetAsAsync<string>("mail", "Host", cancellationToken);
        var rewrittenGenerated = await reader.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken);

        // Assert
        Assert.Equal(value, plain);
        Assert.Equal(value, plainGenerated);
        Assert.Equal(value, encrypted);
        Assert.Equal(value, encryptedGenerated);
        Assert.Equal(value, remote);
        Assert.Equal(value, remoteGenerated);
        Assert.Equal(value, keyed);
        Assert.Equal(value, keyedGenerated);
        Assert.Equal(value, assembled.Host);
        Assert.Equal(value, rewritten);
        Assert.Equal(value, rewrittenGenerated);
    }

    [Theory]
    [MemberData(nameof(ConfigurationSettingExamples.StringValues), MemberType = typeof(ConfigurationSettingExamples))]
    public async Task GetAsAsync_RawString_PreservesTextAsync(string value, bool useSourceGeneration)
    {
        // Arrange
        using var _fixture = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        _fixture.AddNyssaConfiguration();
        var configuration = _fixture.Host.GetRequiredService<IConfiguration>();
        // SetAsync accepts unencoded text, including text that resembles JSON.
        await configuration.SetAsync("mail", "Host", value, IConfiguration.Plain, cancellationToken);
        var reader = _fixture.RefreshedConfigurationService();
        var raw = await reader.GetAsync("mail", "Host", cancellationToken);
        var typed = useSourceGeneration
            ? await reader.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken)
            : await reader.GetAsAsync<string>("mail", "Host", cancellationToken);

        // Assert
        Assert.Equal(value, raw);
        Assert.Equal(value, typed);
    }

    [Theory]
    [InlineData("unquoted", false)]
    [InlineData("unquoted", true)]
    [InlineData("123", false)]
    [InlineData("123", true)]
    [InlineData("null", false)]
    [InlineData("null", true)]
    public async Task GetAsAsync_InvalidJsonString_ThrowsInsteadOfReturningRawTextAsync(string storedValue, bool useSourceGeneration)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using var _fixture = new Workstation { VaultPackage = vaults };
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        _fixture.AddNyssaConfiguration(options => options.Bootstrap.KeyVault = new VaultOptions
        {
            Name = "mailVault", Kind = "fake",
        });
        var configuration = _fixture.Host.GetRequiredService<IConfiguration>();
        // A keyed typed write marks its provider value as JSON; corrupt that remote value.
        await configuration.SetAsAsync("mail", "Host", "smtp.example.test", "mailVault", cancellationToken);
        vaults.Created[0].Values["mail/Host"] = storedValue;
        var exception = await Record.ExceptionAsync(async () =>
        {
            _ = useSourceGeneration
                ? await configuration.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken)
                : await configuration.GetAsAsync<string>("mail", "Host", cancellationToken);
        });

        // Assert
        var failure = Assert.IsType<ResultException>(exception);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(failure.Errors).Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsAsync_RawJsonObject_PreservesTypedDeserializationAsync(bool useSourceGeneration)
    {
        // Arrange
        using var _fixture = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        _fixture.AddNyssaConfiguration();
        var configuration = _fixture.Host.GetRequiredService<IConfiguration>();
        // Existing callers may store JSON text directly and deserialize it as a non-string type.
        await configuration.SetAsync("mail", "server",
            """{"Host":"smtp.example.test","Port":587,"UseTls":true,"Password":"fictional","api-key":"fictional"}""",
            IConfiguration.Plain, cancellationToken);
        var loaded = useSourceGeneration
            ? await configuration.GetAsAsync("mail", "server", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)
            : await configuration.GetAsAsync<NamespaceMailSettings>("mail", "server", cancellationToken);

        // Assert
        Assert.Equal("smtp.example.test", loaded.Host);
        Assert.Equal(587, loaded.Port);
        Assert.True(loaded.UseTls);
    }
}
