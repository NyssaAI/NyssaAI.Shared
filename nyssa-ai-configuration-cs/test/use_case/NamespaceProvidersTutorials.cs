using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Read and update namespace properties across local and remote providers.</summary>
public sealed class NamespaceProvidersTutorials
{
    [Fact]
    public async Task SetAsAsync_GenericJsonMixedProviders_PreservesProtectedPropertiesAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsync("mail", "Password", settings.Password, "aesLocal", cancellationToken);
            await configuration.SetAsync("mail", "api-key", settings.ApiKey, "mailVault", cancellationToken);

            // Act
            await configuration.SetAsAsync("mail", settings, cancellationToken);
            var loaded = await configuration.GetAsAsync<NamespaceMailSettings>("mail", cancellationToken);

            // Assert
            Assert.Equal(587, loaded.Port);
            Assert.True(loaded.UseTls);
            Assert.Null(loaded.Description);
            Assert.Equal([1, 5], loaded.RetryDelays);
            Assert.Equal("first-password", loaded.Password);
            Assert.Equal("first-api-key", loaded.ApiKey);
            var stored = fixture.ReadDocument("mail");
            Assert.DoesNotContain("first-password", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("first-api-key", stored, StringComparison.Ordinal);
            using (var json = JsonDocument.Parse(stored))
            {
                Assert.False(json.RootElement.TryGetProperty("namespaceSettings", out _));
                Assert.Equal("aesLocal", json.RootElement.GetProperty("Password").GetProperty("provider").GetString());
                Assert.Equal("mailVault", json.RootElement.GetProperty("api-key").GetProperty("provider").GetString());
                Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("Port").GetProperty("value").ValueKind);
            }
        }
    }

    [Fact]
    public async Task SetAsAsync_GeneratedJsonMixedProviders_PreservesProtectedPropertiesAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsync("mail", "Password", settings.Password, "aesLocal", cancellationToken);
            await configuration.SetAsync("mail", "api-key", settings.ApiKey, "mailVault", cancellationToken);

            // Act
            await configuration.SetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                settings,
                cancellationToken
            );
            var loaded = await configuration.GetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                cancellationToken
            );

            // Assert
            Assert.Equal(587, loaded.Port);
            Assert.True(loaded.UseTls);
            Assert.Null(loaded.Description);
            Assert.Equal([1, 5], loaded.RetryDelays);
            Assert.Equal("first-password", loaded.Password);
            Assert.Equal("first-api-key", loaded.ApiKey);
            var stored = fixture.ReadDocument("mail");
            Assert.DoesNotContain("first-password", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("first-api-key", stored, StringComparison.Ordinal);
            using (var json = JsonDocument.Parse(stored))
            {
                Assert.False(json.RootElement.TryGetProperty("namespaceSettings", out _));
                Assert.Equal("aesLocal", json.RootElement.GetProperty("Password").GetProperty("provider").GetString());
                Assert.Equal("mailVault", json.RootElement.GetProperty("api-key").GetProperty("provider").GetString());
                Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("Port").GetProperty("value").ValueKind);
            }
        }
    }

    [Fact]
    public async Task SetAsAsync_ExistingProtectedProperties_UpdatesValuesWithoutChangingProvidersAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsync("mail", "Password", settings.Password, "aesLocal", cancellationToken);
            await configuration.SetAsync("mail", "api-key", settings.ApiKey, "mailVault", cancellationToken);
            await configuration.SetAsAsync("mail", settings, cancellationToken);
            var replacement = settings with
            {
                Host = "123",
                Port = 465,
                Password = "next-password",
                ApiKey = "next-api-key",
            };

            // Act
            await configuration.SetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                replacement,
                cancellationToken
            );
            var loaded = await configuration.GetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                cancellationToken
            );

            // Assert
            Assert.Equal("123", loaded.Host);
            Assert.Equal(465, loaded.Port);
            Assert.Equal("next-password", loaded.Password);
            Assert.Equal("next-api-key", loaded.ApiKey);
            var stored = fixture.ReadDocument("mail");
            Assert.DoesNotContain("next-password", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("next-api-key", stored, StringComparison.Ordinal);
            using (var json = JsonDocument.Parse(stored))
            {
                Assert.Equal("aesLocal", json.RootElement.GetProperty("Password").GetProperty("provider").GetString());
                Assert.Equal("mailVault", json.RootElement.GetProperty("api-key").GetProperty("provider").GetString());
            }
        }
    }

    [Fact]
    public async Task GetAsAsync_KeyedTypedWrites_AssemblesNamespaceDtoAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsAsync("mail", settings, cancellationToken);

            // Act
            await configuration.SetAsAsync("mail", "Port", 465, cancellationToken: cancellationToken);
            await configuration.SetAsAsync(
                "mail",
                "Host",
                "123",
                AppJsonContext.Default.String,
                cancellationToken: cancellationToken
            );
            var loaded = await configuration.GetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                cancellationToken
            );
            var host = await configuration.GetAsAsync("mail", "Host", AppJsonContext.Default.String, cancellationToken);

            // Assert
            Assert.Equal(465, loaded.Port);
            Assert.Equal("123", loaded.Host);
            Assert.Equal("123", host);
        }
    }

    [Fact]
    public async Task MoveAsync_NumberAcrossProviders_PreservesJsonTypeAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsAsync("mail", settings, cancellationToken);

            // Act
            await configuration.MoveAsync("mail", "Port", "aesLocal", cancellationToken);
            await configuration.SetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                settings with
                {
                    Port = 2525,
                },
                cancellationToken
            );
            await configuration.MoveAsync("mail", "Port", "mailVault", cancellationToken);
            var remote = await configuration.GetAsAsync(
                "mail",
                AppJsonContext.Default.NamespaceMailSettings,
                cancellationToken
            );
            await configuration.MoveAsync("mail", "Port", cancellationToken: cancellationToken);
            var local = await configuration.GetAsAsync<NamespaceMailSettings>("mail", cancellationToken);

            // Assert
            Assert.Equal(2525, remote.Port);
            Assert.Equal(2525, local.Port);
            using (var json = JsonDocument.Parse(fixture.ReadDocument("mail")))
            {
                Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("Port").GetProperty("value").ValueKind);
            }
        }
    }

    [Fact]
    public async Task GetAsAsync_InvalidRemoteJson_RejectsNamespaceReadAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsAsync("mail", settings, cancellationToken);
            await configuration.MoveAsync("mail", "Port", "mailVault", cancellationToken);
            Assert.Single(vaults.Created).Values["mail/Port"] = "invalid-json";

            // Act / Assert
            await Assert.ThrowsAsync<ResultException>(() =>
                configuration
                    .GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)
                    .AsTask()
            );
        }
    }

    [Fact]
    public async Task GetAsAsync_MissingRemoteSecret_RejectsNamespaceReadAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new NamespaceMailSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                UseTls = true,
                Password = "first-password",
                ApiKey = "first-api-key",
                RetryDelays = [1, 5],
            };

            await configuration.SetAsAsync("mail", settings, cancellationToken);
            await configuration.MoveAsync("mail", "api-key", "mailVault", cancellationToken);
            Assert.Single(vaults.Created).Values.Remove("mail/api-key");

            // Act / Assert
            await Assert.ThrowsAsync<ResultException>(() =>
                configuration
                    .GetAsAsync("mail", AppJsonContext.Default.NamespaceMailSettings, cancellationToken)
                    .AsTask()
            );
        }
    }
}
