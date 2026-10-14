using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>NamespaceSettingsTutorials demonstrates a namespace's default settings object.</summary>
public sealed class NamespaceSettingsTutorials
{
    /// <summary>Existing string-encoded settings remain readable and become individual properties on the next typed save.</summary>
    [Fact]
    public async Task GetAsAsync_LegacyNamespaceSettings_ReadsAndUpdatesAsync()
    {
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            const string legacy = """
                {"Host":"smtp.example.test","Port":587,"Security":{"IsTlsEnabled":true,"Credentials":{"UserName":"example","Password":"fictional"}}}
                """;
            await configuration.SetAsync("mail", "namespaceSettings", legacy, cancellationToken: cancellationToken);
            var loaded = await fixture
                .RefreshedConfigurationService()
                .GetAsAsync("mail", AppJsonContext.Default.MailServerSettings, cancellationToken);
            Assert.Equal("smtp.example.test", loaded.Host);
            await configuration.SetAsAsync(
                "mail",
                AppJsonContext.Default.MailServerSettings,
                loaded,
                cancellationToken
            );
            using (var json = System.Text.Json.JsonDocument.Parse(fixture.ReadDocument("mail")))
            {
                Assert.Equal(
                    System.Text.Json.JsonValueKind.Object,
                    json.RootElement.GetProperty("Security").GetProperty("value").ValueKind
                );
            }
        }
    }

    /// <summary>String values can use the shorthand with optional cancellation omitted.</summary>
    [Fact]
    public async Task SetAsAsync_GenericJsonStringNamespaceSettings_AllowsOmittedCancellationAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var typeInfo = AppJsonContext.Default.String;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "obsolete", "old setting", cancellationToken: cancellationToken);
            // Omit cancellation intentionally to verify overload selection for the minimal calls.
#pragma warning disable xUnit1051
            await configuration.SetAsAsync("mail", "weekly report");
#pragma warning restore xUnit1051
            var stored = await fixture.RefreshedConfigurationService().GetAsAsync("mail", typeInfo, cancellationToken);
            var replacementKeys = await configuration.ListAsync("mail", cancellationToken);
            // Keyed string writes use an explicit key and default storage.
            await configuration.SetAsAsync(
                "mail",
                "label",
                "Mail module",
                typeInfo,
                cancellationToken: cancellationToken
            );
            var label = await configuration.GetAsAsync("mail", "label", typeInfo, cancellationToken);

            // Assert
            Assert.Equal(["namespaceSettings"], replacementKeys);
            Assert.Equal("weekly report", stored);
            Assert.Equal("Mail module", label);
        }
    }

    /// <summary>String values can use the shorthand with optional cancellation omitted.</summary>
    [Fact]
    public async Task SetAsAsync_GeneratedJsonStringNamespaceSettings_AllowsOmittedCancellationAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var typeInfo = AppJsonContext.Default.String;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "obsolete", "old setting", cancellationToken: cancellationToken);
            // Omit cancellation intentionally to verify overload selection for the minimal calls.
#pragma warning disable xUnit1051
            await configuration.SetAsAsync("mail", typeInfo, "weekly report");
#pragma warning restore xUnit1051
            var stored = await fixture.RefreshedConfigurationService().GetAsAsync("mail", typeInfo, cancellationToken);
            var replacementKeys = await configuration.ListAsync("mail", cancellationToken);
            // Keyed string writes use an explicit key and default storage.
            await configuration.SetAsAsync(
                "mail",
                "label",
                "Mail module",
                typeInfo,
                cancellationToken: cancellationToken
            );
            var label = await configuration.GetAsAsync("mail", "label", typeInfo, cancellationToken);

            // Assert
            Assert.Equal(["namespaceSettings"], replacementKeys);
            Assert.Equal("weekly report", stored);
            Assert.Equal("Mail module", label);
        }
    }

    /// <summary>A complete object persists as plaintext as individual properties without replacing unrelated entries.</summary>
    [Fact]
    public async Task MergeSettingsAsync_GenericJsonNamespaceSettings_PersistsPropertiesAndPreservesOtherEntriesAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new MailServerSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                Security = new MailSecuritySettings
                {
                    IsTlsEnabled = true,
                    Credentials = new MailCredentials { UserName = "example", Password = "fictional" },
                },
            };
            var typeInfo = AppJsonContext.Default.MailServerSettings;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            // Other keys in this namespace and settings in another namespace remain independent.
            await configuration.SetAsync("mail", "label", "Mail module", cancellationToken: cancellationToken);
            await configuration.SetAsAsync("other", settings with { Port = 25 }, cancellationToken);
            // Merge updates the supplied properties and preserves omitted entries. AOT callers pass type information.
            await configuration.MergeSettingsAsync("mail", settings, cancellationToken);
            var firstDocument = fixture.ReadDocument("mail");
            using (var json = System.Text.Json.JsonDocument.Parse(firstDocument))
            {
                var dto = json.RootElement;
                var reader = fixture.RefreshedConfigurationService();
                var loaded = await reader.GetAsAsync<MailServerSettings>("mail", cancellationToken);
                // Saving again updates each property.
                await configuration.MergeSettingsAsync("mail", settings with { Port = 465 }, cancellationToken);
                var refreshed = fixture.RefreshedConfigurationService();
                var stored = await refreshed.GetAsAsync<MailServerSettings>("mail", cancellationToken);
                var other = await refreshed.GetAsAsync<MailServerSettings>("other", cancellationToken);
                var label = await refreshed.GetAsync("mail", "label", cancellationToken);
                var keys = await refreshed.ListAsync("mail", cancellationToken);

                // Assert
                Assert.Equal(System.Text.Json.JsonValueKind.Object, dto.ValueKind);
                Assert.Equal("smtp.example.test", dto.GetProperty("Host").GetProperty("value").GetString());
                Assert.True(dto.GetProperty("Security").GetProperty("value").GetProperty("IsTlsEnabled").GetBoolean());
                Assert.Equal("smtp.example.test", loaded.Host);
                Assert.Equal(587, loaded.Port);
                Assert.True(loaded.Security.IsTlsEnabled);
                Assert.Equal("example", loaded.Security.Credentials.UserName);
                Assert.Equal("fictional", loaded.Security.Credentials.Password);
                Assert.Contains("smtp.example.test", firstDocument, StringComparison.Ordinal);
                Assert.DoesNotContain("aesLocal", firstDocument, StringComparison.Ordinal);
                Assert.Equal(465, stored.Port);
                Assert.Equal(25, other.Port);
                Assert.Equal("Mail module", label);
                Assert.Equal(["Host", "Port", "Security", "label"], keys);
            }
        }
    }

    /// <summary>A complete object persists as plaintext as individual properties without replacing unrelated entries.</summary>
    [Fact]
    public async Task MergeSettingsAsync_GeneratedJsonNamespaceSettings_PersistsPropertiesAndPreservesOtherEntriesAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new MailServerSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                Security = new MailSecuritySettings
                {
                    IsTlsEnabled = true,
                    Credentials = new MailCredentials { UserName = "example", Password = "fictional" },
                },
            };
            var typeInfo = AppJsonContext.Default.MailServerSettings;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            // Other keys in this namespace and settings in another namespace remain independent.
            await configuration.SetAsync("mail", "label", "Mail module", cancellationToken: cancellationToken);
            await configuration.SetAsAsync("other", settings with { Port = 25 }, cancellationToken);
            // Merge updates the supplied properties and preserves omitted entries. AOT callers pass type information.
            await configuration.MergeSettingsAsync("mail", typeInfo, settings, cancellationToken);
            var firstDocument = fixture.ReadDocument("mail");
            using (var json = System.Text.Json.JsonDocument.Parse(firstDocument))
            {
                var dto = json.RootElement;
                var reader = fixture.RefreshedConfigurationService();
                var loaded = await reader.GetAsAsync("mail", typeInfo, cancellationToken);
                // Saving again updates each property.
                await (
                    configuration.MergeSettingsAsync("mail", typeInfo, settings with { Port = 465 }, cancellationToken)
                );
                var refreshed = fixture.RefreshedConfigurationService();
                var stored = await refreshed.GetAsAsync<MailServerSettings>("mail", cancellationToken);
                var other = await refreshed.GetAsAsync<MailServerSettings>("other", cancellationToken);
                var label = await refreshed.GetAsync("mail", "label", cancellationToken);
                var keys = await refreshed.ListAsync("mail", cancellationToken);

                // Assert
                Assert.Equal(System.Text.Json.JsonValueKind.Object, dto.ValueKind);
                Assert.Equal("smtp.example.test", dto.GetProperty("Host").GetProperty("value").GetString());
                Assert.True(dto.GetProperty("Security").GetProperty("value").GetProperty("IsTlsEnabled").GetBoolean());
                Assert.Equal("smtp.example.test", loaded.Host);
                Assert.Equal(587, loaded.Port);
                Assert.True(loaded.Security.IsTlsEnabled);
                Assert.Equal("example", loaded.Security.Credentials.UserName);
                Assert.Equal("fictional", loaded.Security.Credentials.Password);
                Assert.Contains("smtp.example.test", firstDocument, StringComparison.Ordinal);
                Assert.DoesNotContain("aesLocal", firstDocument, StringComparison.Ordinal);
                Assert.Equal(465, stored.Port);
                Assert.Equal(25, other.Port);
                Assert.Equal("Mail module", label);
                Assert.Equal(["Host", "Port", "Security", "label"], keys);
            }
        }
    }

    /// <summary>The plaintext shorthand preserves the existing provider-conflict rule.</summary>
    [Fact]
    public async Task SetAsAsync_GenericJsonEncryptedNamespaceSettings_RejectsPlaintextReplacementAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var credentials = new MailServerSettings
            {
                Host = "smtp.private.test",
                Port = 465,
                Security = new MailSecuritySettings
                {
                    IsTlsEnabled = true,
                    Credentials = new MailCredentials { UserName = "example", Password = "fictional-secret" },
                },
            };
            var typeInfo = AppJsonContext.Default.MailServerSettings;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            // The keyed API can explicitly encrypt the reserved entry, just like any other entry.
            await configuration.SetAsAsync("mail", "namespaceSettings", credentials, "aesLocal", cancellationToken);
            var replaced = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.SetAsAsync("mail", credentials with { Port = 587 }, cancellationToken);
            });
            // Namespace reads follow the stored provider, including encryption.
            var stored = await configuration.GetAsAsync<MailServerSettings>("mail", cancellationToken);
            var document = fixture.ReadDocument("mail");

            // Assert
            Assert.Equal(ErrorCategory.Conflict, Assert.Single(replaced.Errors).Category);
            Assert.Equal(465, stored.Port);
            Assert.Equal("fictional-secret", stored.Security.Credentials.Password);
            Assert.Contains("aesLocal", document, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-secret", document, StringComparison.Ordinal);
        }
    }

    /// <summary>The plaintext shorthand preserves the existing provider-conflict rule.</summary>
    [Fact]
    public async Task SetAsAsync_GeneratedJsonEncryptedNamespaceSettings_RejectsPlaintextReplacementAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var credentials = new MailServerSettings
            {
                Host = "smtp.private.test",
                Port = 465,
                Security = new MailSecuritySettings
                {
                    IsTlsEnabled = true,
                    Credentials = new MailCredentials { UserName = "example", Password = "fictional-secret" },
                },
            };
            var typeInfo = AppJsonContext.Default.MailServerSettings;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            // The keyed API can explicitly encrypt the reserved entry, just like any other entry.
            await configuration.SetAsAsync("mail", "namespaceSettings", credentials, "aesLocal", cancellationToken);
            var replaced = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.SetAsAsync("mail", typeInfo, credentials with { Port = 587 }, cancellationToken);
            });
            // Namespace reads follow the stored provider, including encryption.
            var stored = await configuration.GetAsAsync("mail", typeInfo, cancellationToken);
            var document = fixture.ReadDocument("mail");

            // Assert
            Assert.Equal(ErrorCategory.Conflict, Assert.Single(replaced.Errors).Category);
            Assert.Equal(465, stored.Port);
            Assert.Equal("fictional-secret", stored.Security.Credentials.Password);
            Assert.Contains("aesLocal", document, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-secret", document, StringComparison.Ordinal);
        }
    }

    /// <summary>Reading default settings does not invent an object when the entry is absent.</summary>
    [Fact]
    public async Task GetAsAsync_GenericJsonMissingNamespaceSettings_ThrowsForMissingSettingsAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            // A module supplies its initial object with SetAsAsync before trying to read it.
            var missing = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.GetAsAsync<MailServerSettings>("mail", cancellationToken);
            });

            // Assert
            Assert.Equal(ErrorCategory.NotFound, Assert.Single(missing.Errors).Category);
        }
    }

    /// <summary>Reading default settings does not invent an object when the entry is absent.</summary>
    [Fact]
    public async Task GetAsAsync_GeneratedJsonMissingNamespaceSettings_ThrowsForMissingSettingsAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(fixture.Locate);
            fixture.StartApplication(services);
            IConfiguration configuration = fixture.Host.GetRequiredService<IConfiguration>();
            // A module supplies its initial object with SetAsAsync before trying to read it.
            var missing = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.GetAsAsync("mail", AppJsonContext.Default.MailServerSettings, cancellationToken);
            });

            // Assert
            Assert.Equal(ErrorCategory.NotFound, Assert.Single(missing.Errors).Category);
        }
    }
}
