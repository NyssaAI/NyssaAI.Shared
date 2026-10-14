using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Default writes, inherited providers, and moves back to default storage.</summary>
public sealed class ConfigurationDefaultsTutorials
{
    [Theory]
    [InlineData("aesLocal")]
    [InlineData("mailVault")]
    public async Task SetAsAsync_GenericJsonWholeDto_PreservesProviderUntilMovedToDefaultAsync(string provider)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "fictional-password",
                ApiKey = "fictional-api-key",
            };

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsAsync("mail", "server", settings, cancellationToken: cancellationToken);
            using (var initialJson = JsonDocument.Parse(fixture.ReadDocument("mail")))
            {
                // Move chooses storage; later writes only supply the updated value.
                await configuration.MoveAsync("mail", "server", provider, cancellationToken);
                settings = settings with { Port = 465 };
                await configuration.SetAsAsync("mail", "server", settings, cancellationToken: cancellationToken);
                using (var protectedJson = JsonDocument.Parse(fixture.ReadDocument("mail")))
                {
                    var protectedSettings = await configuration.GetAsAsync(
                        "mail",
                        "server",
                        AppJsonContext.Default.NamespaceMailSettings,
                        cancellationToken
                    );
                    // Omitting the move destination returns to default storage and withdraws the provider copy.
                    var moved = await configuration.MoveAsync("mail", "server", cancellationToken: cancellationToken);
                    using (var defaultJson = JsonDocument.Parse(fixture.ReadDocument("mail")))
                    {
                        var loaded = await fixture
                            .RefreshedConfigurationService()
                            .GetAsAsync<NamespaceMailSettings>("mail", "server", cancellationToken);

                        // Assert
                        Assert.Single(initialJson.RootElement.GetProperty("server").EnumerateObject());
                        Assert.Equal(
                            587,
                            initialJson
                                .RootElement.GetProperty("server")
                                .GetProperty("value")
                                .GetProperty("Port")
                                .GetInt32()
                        );
                        Assert.Equal(
                            provider,
                            protectedJson.RootElement.GetProperty("server").GetProperty("provider").GetString()
                        );
                        Assert.True(protectedJson.RootElement.GetProperty("server").GetProperty("json").GetBoolean());
                        Assert.Equal(465, protectedSettings.Port);
                        Assert.Equal(provider, moved.From);
                        Assert.Empty(moved.To);
                        Assert.True(moved.SourceWithdrawn);
                        Assert.Single(defaultJson.RootElement.GetProperty("server").EnumerateObject());
                        Assert.Equal(
                            JsonValueKind.Object,
                            defaultJson.RootElement.GetProperty("server").GetProperty("value").ValueKind
                        );
                        Assert.Equal("smtp.example.test", loaded.Host);
                        Assert.Equal(465, loaded.Port);
                        Assert.Equal("fictional-password", loaded.Password);
                        Assert.Empty(vaults.Created[0].Values);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("aesLocal")]
    [InlineData("mailVault")]
    public async Task SetAsAsync_GeneratedJsonWholeDto_PreservesProviderUntilMovedToDefaultAsync(string provider)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "fictional-password",
                ApiKey = "fictional-api-key",
            };

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsAsync(
                "mail",
                "server",
                settings,
                AppJsonContext.Default.NamespaceMailSettings,
                cancellationToken: cancellationToken
            );
            using (var initialJson = JsonDocument.Parse(fixture.ReadDocument("mail")))
            {
                // Move chooses storage; later writes only supply the updated value.
                await configuration.MoveAsync("mail", "server", provider, cancellationToken);
                settings = settings with { Port = 465 };
                await configuration.SetAsAsync(
                    "mail",
                    "server",
                    settings,
                    AppJsonContext.Default.NamespaceMailSettings,
                    cancellationToken: cancellationToken
                );
                using (var protectedJson = JsonDocument.Parse(fixture.ReadDocument("mail")))
                {
                    var protectedSettings = await configuration.GetAsAsync(
                        "mail",
                        "server",
                        AppJsonContext.Default.NamespaceMailSettings,
                        cancellationToken
                    );
                    // Omitting the move destination returns to default storage and withdraws the provider copy.
                    var moved = await configuration.MoveAsync("mail", "server", cancellationToken: cancellationToken);
                    using (var defaultJson = JsonDocument.Parse(fixture.ReadDocument("mail")))
                    {
                        var loaded = await fixture
                            .RefreshedConfigurationService()
                            .GetAsAsync<NamespaceMailSettings>("mail", "server", cancellationToken);

                        // Assert
                        Assert.Single(initialJson.RootElement.GetProperty("server").EnumerateObject());
                        Assert.Equal(
                            587,
                            initialJson
                                .RootElement.GetProperty("server")
                                .GetProperty("value")
                                .GetProperty("Port")
                                .GetInt32()
                        );
                        Assert.Equal(
                            provider,
                            protectedJson.RootElement.GetProperty("server").GetProperty("provider").GetString()
                        );
                        Assert.True(protectedJson.RootElement.GetProperty("server").GetProperty("json").GetBoolean());
                        Assert.Equal(465, protectedSettings.Port);
                        Assert.Equal(provider, moved.From);
                        Assert.Empty(moved.To);
                        Assert.True(moved.SourceWithdrawn);
                        Assert.Single(defaultJson.RootElement.GetProperty("server").EnumerateObject());
                        Assert.Equal(
                            JsonValueKind.Object,
                            defaultJson.RootElement.GetProperty("server").GetProperty("value").ValueKind
                        );
                        Assert.Equal("smtp.example.test", loaded.Host);
                        Assert.Equal(465, loaded.Port);
                        Assert.Equal("fictional-password", loaded.Password);
                        Assert.Empty(vaults.Created[0].Values);
                    }
                }
            }
        }
    }
}
