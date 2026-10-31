using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Replacement and merging act on the serialized top-level properties of a namespace.</summary>
public sealed class NamespaceWriteTutorials
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceWrite_GenericJsonPartialObject_ReplacesOrPreservesOmittedEntriesAsync(bool merge)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (
                var initial = JsonDocument.Parse(
                    """{"Host":"smtp.old.test","Port":587,"ApiKey":"fictional-key","Retry":{"Attempts":3,"DelaySeconds":5},"Description":"old"}"""
                )
            )
            using (
                var update = JsonDocument.Parse(
                    """{"Host":"smtp.new.test","Retry":{"Attempts":8},"Description":null}"""
                )
            )
            {
                var typeInfo = AppJsonContext.Default.JsonElement;

                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(options =>
                {
                    fixture.Locate(options);
                    options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
                });
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsAsync("mail", typeInfo, initial.RootElement, cancellationToken);
                await configuration.MoveAsync("mail", "Host", "aesLocal", cancellationToken);
                await configuration.MoveAsync("mail", "ApiKey", "mailVault", cancellationToken);
                await configuration.SetAsync("other", "Status", "unchanged", cancellationToken: cancellationToken);
                // Set replaces the namespace; Merge updates only the supplied top-level properties.
                if (merge)
                    await configuration.MergeSettingsAsync("mail", update.RootElement, cancellationToken);
                else
                    await configuration.SetAsAsync("mail", update.RootElement, cancellationToken);
                var loaded = await configuration.GetAsAsync("mail", typeInfo, cancellationToken);
                using (var document = JsonDocument.Parse(fixture.ReadDocument("mail")))
                {
                    var other = await configuration.GetAsync("other", "Status", cancellationToken);

                    // Assert
                    Assert.Equal(merge, loaded.TryGetProperty("Port", out _));
                    Assert.Equal(merge, loaded.TryGetProperty("ApiKey", out _));
                    Assert.Equal("smtp.new.test", loaded.GetProperty("Host").GetString());
                    Assert.Equal(
                        "aesLocal",
                        document.RootElement.GetProperty("Host").GetProperty("provider").GetString()
                    );
                    Assert.True(document.RootElement.GetProperty("Host").GetProperty("encrypted").GetBoolean());
                    Assert.Equal(8, loaded.GetProperty("Retry").GetProperty("Attempts").GetInt32());
                    Assert.False(loaded.GetProperty("Retry").TryGetProperty("DelaySeconds", out _));
                    Assert.Equal(JsonValueKind.Null, loaded.GetProperty("Description").ValueKind);
                    Assert.Equal("unchanged", other);
                    // Namespace replacement prunes local entries without requesting remote deletion.
                    Assert.Equal("fictional-key", vaults.Created[0].Values["mail/ApiKey"]);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceWrite_GeneratedJsonPartialObject_ReplacesOrPreservesOmittedEntriesAsync(bool merge)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (
                var initial = JsonDocument.Parse(
                    """{"Host":"smtp.old.test","Port":587,"ApiKey":"fictional-key","Retry":{"Attempts":3,"DelaySeconds":5},"Description":"old"}"""
                )
            )
            using (
                var update = JsonDocument.Parse(
                    """{"Host":"smtp.new.test","Retry":{"Attempts":8},"Description":null}"""
                )
            )
            {
                var typeInfo = AppJsonContext.Default.JsonElement;

                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(options =>
                {
                    fixture.Locate(options);
                    options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
                });
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsAsync("mail", typeInfo, initial.RootElement, cancellationToken);
                await configuration.MoveAsync("mail", "Host", "aesLocal", cancellationToken);
                await configuration.MoveAsync("mail", "ApiKey", "mailVault", cancellationToken);
                await configuration.SetAsync("other", "Status", "unchanged", cancellationToken: cancellationToken);
                // Set replaces the namespace; Merge updates only the supplied top-level properties.
                if (merge)
                    await configuration.MergeSettingsAsync("mail", typeInfo, update.RootElement, cancellationToken);
                else
                    await configuration.SetAsAsync("mail", typeInfo, update.RootElement, cancellationToken);
                var loaded = await configuration.GetAsAsync("mail", typeInfo, cancellationToken);
                using (var document = JsonDocument.Parse(fixture.ReadDocument("mail")))
                {
                    var other = await configuration.GetAsync("other", "Status", cancellationToken);

                    // Assert
                    Assert.Equal(merge, loaded.TryGetProperty("Port", out _));
                    Assert.Equal(merge, loaded.TryGetProperty("ApiKey", out _));
                    Assert.Equal("smtp.new.test", loaded.GetProperty("Host").GetString());
                    Assert.Equal(
                        "aesLocal",
                        document.RootElement.GetProperty("Host").GetProperty("provider").GetString()
                    );
                    Assert.True(document.RootElement.GetProperty("Host").GetProperty("encrypted").GetBoolean());
                    Assert.Equal(8, loaded.GetProperty("Retry").GetProperty("Attempts").GetInt32());
                    Assert.False(loaded.GetProperty("Retry").TryGetProperty("DelaySeconds", out _));
                    Assert.Equal(JsonValueKind.Null, loaded.GetProperty("Description").ValueKind);
                    Assert.Equal("unchanged", other);
                    // Namespace replacement prunes local entries without requesting remote deletion.
                    Assert.Equal("fictional-key", vaults.Created[0].Values["mail/ApiKey"]);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceWrite_GenericJsonEmptyObject_ClearsOnlyOnReplacementAsync(bool merge)
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (var empty = JsonDocument.Parse("{}"))
            {
                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(fixture.Locate);
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync("mail", "Host", "smtp.example.test", cancellationToken: cancellationToken);
                if (merge)
                    await configuration.MergeSettingsAsync("mail", empty.RootElement, cancellationToken);
                else
                    await configuration.SetAsAsync("mail", empty.RootElement, cancellationToken);
                var keys = await fixture.RefreshedConfigurationService().ListAsync("mail", cancellationToken);

                // Assert
                Assert.Equal(merge ? ["Host"] : Array.Empty<string>(), keys);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceWrite_GeneratedJsonEmptyObject_ClearsOnlyOnReplacementAsync(bool merge)
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (var empty = JsonDocument.Parse("{}"))
            {
                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(fixture.Locate);
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync("mail", "Host", "smtp.example.test", cancellationToken: cancellationToken);
                if (merge)
                    await (
                        configuration.MergeSettingsAsync(
                            "mail",
                            AppJsonContext.Default.JsonElement,
                            empty.RootElement,
                            cancellationToken
                        )
                    );
                else
                    await (
                        configuration.SetAsAsync(
                            "mail",
                            AppJsonContext.Default.JsonElement,
                            empty.RootElement,
                            cancellationToken
                        )
                    );
                var keys = await fixture.RefreshedConfigurationService().ListAsync("mail", cancellationToken);

                // Assert
                Assert.Equal(merge ? ["Host"] : Array.Empty<string>(), keys);
            }
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    public async Task MergeSettingsAsync_GenericJsonNonObject_RejectsWithoutChangingNamespaceAsync(string input)
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (var update = JsonDocument.Parse(input))
            {
                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(fixture.Locate);
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync("mail", "Host", "smtp.example.test", cancellationToken: cancellationToken);
                var before = fixture.ReadDocument("mail");
                var failure = await Assert.ThrowsAsync<ResultException>(async () =>
                {
                    await configuration.MergeSettingsAsync("mail", update.RootElement, cancellationToken);
                });
                var after = fixture.ReadDocument("mail");

                // Assert

                Assert.Equal(ErrorCategory.Validation, Assert.Single(failure.Errors).Category);
                Assert.Equal(before, after);
            }
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    public async Task MergeSettingsAsync_GeneratedJsonNonObject_RejectsWithoutChangingNamespaceAsync(string input)
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (var update = JsonDocument.Parse(input))
            {
                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(fixture.Locate);
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync("mail", "Host", "smtp.example.test", cancellationToken: cancellationToken);
                var before = fixture.ReadDocument("mail");
                var failure = await Assert.ThrowsAsync<ResultException>(async () =>
                {
                    await (
                        configuration.MergeSettingsAsync(
                            "mail",
                            AppJsonContext.Default.JsonElement,
                            update.RootElement,
                            cancellationToken
                        )
                    );
                });
                var after = fixture.ReadDocument("mail");

                // Assert

                Assert.Equal(ErrorCategory.Validation, Assert.Single(failure.Errors).Category);
                Assert.Equal(before, after);
            }
        }
    }

    [Fact]
    public async Task MergeSettingsAsync_GenericJsonLegacyObject_PreservesOmittedPropertiesAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (var update = JsonDocument.Parse("""{"Host":"smtp.new.test"}"""))
            {
                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(fixture.Locate);
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync(
                    "mail",
                    "namespaceSettings",
                    """{"Host":"smtp.old.test","Port":587}""",
                    cancellationToken: cancellationToken
                );
                await configuration.MergeSettingsAsync("mail", update.RootElement, cancellationToken);
                var loaded = await fixture
                    .RefreshedConfigurationService()
                    .GetAsAsync("mail", AppJsonContext.Default.JsonElement, cancellationToken);
                var keys = await configuration.ListAsync("mail", cancellationToken);

                // Assert
                Assert.Equal("smtp.new.test", loaded.GetProperty("Host").GetString());
                Assert.Equal(587, loaded.GetProperty("Port").GetInt32());
                Assert.Equal(["Host", "Port"], keys);
            }
        }
    }

    [Fact]
    public async Task MergeSettingsAsync_GeneratedJsonLegacyObject_PreservesOmittedPropertiesAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using (var update = JsonDocument.Parse("""{"Host":"smtp.new.test"}"""))
            {
                // Act
                var services = fixture.CreateServices();
                services.AddNyssaConfiguration(fixture.Locate);
                fixture.StartApplication(services);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync(
                    "mail",
                    "namespaceSettings",
                    """{"Host":"smtp.old.test","Port":587}""",
                    cancellationToken: cancellationToken
                );
                await (
                    configuration.MergeSettingsAsync(
                        "mail",
                        AppJsonContext.Default.JsonElement,
                        update.RootElement,
                        cancellationToken
                    )
                );
                var loaded = await fixture
                    .RefreshedConfigurationService()
                    .GetAsAsync("mail", AppJsonContext.Default.JsonElement, cancellationToken);
                var keys = await configuration.ListAsync("mail", cancellationToken);

                // Assert
                Assert.Equal("smtp.new.test", loaded.GetProperty("Host").GetString());
                Assert.Equal(587, loaded.GetProperty("Port").GetInt32());
                Assert.Equal(["Host", "Port"], keys);
            }
        }
    }
}
