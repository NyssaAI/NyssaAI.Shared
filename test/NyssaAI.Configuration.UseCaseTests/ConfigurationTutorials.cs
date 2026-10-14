using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>
/// Storing and reading settings and secrets through <see cref="IConfiguration"/>, once setup
/// (<see cref="SetupTutorials"/>) has made it injectable. UC-003 through UC-009.
/// </summary>
public sealed class ConfigurationTutorials
{
    /// <summary>UC-003. Each basic value comes back as its own type, read from a scope other than the one that wrote it.</summary>
    [Theory]
    [MemberData(nameof(ConfigurationSettingExamples.BasicValues), MemberType = typeof(ConfigurationSettingExamples))]
    public async Task SetAsAsync_BasicValue_RoundTripsAcrossScopesAsync<TValue>(
        TValue settingValue,
        TValue expectedValue
    )
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            // SetAsAsync serializes the typed value into namespace "application", key "preference".
            // Plain is appropriate for these nonsecret preferences; secret values use the encryption provider.
            await configuration.SetAsAsync(
                "application",
                "preference",
                settingValue,
                cancellationToken: cancellationToken
            );
            // A fresh caller scope reads the persisted value rather than relying on the original client.
            var stored = await workstation
                .RefreshedConfigurationService()
                .GetAsAsync<TValue>("application", "preference", cancellationToken);

            // Assert
            Assert.Equal(expectedValue, stored);
        }
    }

    /// <summary>UC-004. A three-level object is one entry, restored intact.</summary>
    [Fact]
    public async Task SetAsAsync_NestedSettings_RestoresAllLevelsAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new MailServerSettings
            {
                Host = "smtp.example.test",
                Port = 587,
                Security = new MailSecuritySettings
                {
                    IsTlsEnabled = true,
                    Credentials = new MailCredentials
                    {
                        UserName = "tutorial@example.test",
                        Password = "fictional-tutorial-password",
                    },
                },
            };

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            // Store the complete nested object as one "mail/server" entry using plain JSON.
            await workstation
                .Host.GetRequiredService<IConfiguration>()
                .SetAsAsync("mail", "server", settings, cancellationToken: cancellationToken);
            // A fresh caller scope reads the persisted value rather than relying on the original client.
            var stored = await workstation
                .RefreshedConfigurationService()
                .GetAsAsync<MailServerSettings>("mail", "server", cancellationToken);

            // Assert
            Assert.Equal("smtp.example.test", stored.Host);
            Assert.Equal(587, stored.Port);
            Assert.True(stored.Security.IsTlsEnabled);
            Assert.Equal("tutorial@example.test", stored.Security.Credentials.UserName);
            Assert.Equal("fictional-tutorial-password", stored.Security.Credentials.Password);
        }
    }

    /// <summary>UC-005. A secret is sealed by naming "aesLocal" once; an update keeps that provider without
    /// naming it; the app handles no key and constructs no cipher.
    /// </summary>
    [Fact]
    public async Task SetAsync_EncryptedEntry_InheritsProviderAndRejectsReplacementAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            // Select encryption when creating this key
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-password-v1",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );
            // An update omits the provider and inherits the recorded one
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-password-v2",
                cancellationToken: cancellationToken
            );
            var read = await configuration.GetAsync("mail", "password", cancellationToken);
            var document = workstation.ReadDocument("mail");
            // Naming a *different* provider on an existing entry is refused — that is what MoveAsync is for
            var elsewhere = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.SetAsync(
                    "mail",
                    "password",
                    "v3",
                    provider: "otherProvider",
                    cancellationToken: cancellationToken
                );
            });

            // Assert
            Assert.Equal("fictional-password-v2", read);
            Assert.DoesNotContain("fictional-password-v1", document, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-password-v2", document, StringComparison.Ordinal);
            Assert.Contains("\"aesLocal\"", document, StringComparison.Ordinal);
            Assert.Equal(ErrorCategory.Conflict, elsewhere.Errors[0].Category);
        }
    }

    /// <summary>UC-006. A new process, same application name, opens what the previous process sealed — the key
    /// is in the credential store, not in either process.
    /// </summary>
    [Fact]
    public async Task GetAsync_RestartedApplication_ReusesEncryptionKeyAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            await workstation
                .Host.GetRequiredService<IConfiguration>()
                .SetAsync(
                    "mail",
                    "password",
                    "fictional-password",
                    provider: "aesLocal",
                    cancellationToken: cancellationToken
                );
            workstation.StopApplication();
            workstation.Log.Messages.Clear();
            // A second process: a new container on the same workstation
            var services2 = workstation.CreateServices();
            services2.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services2);
            var read = await workstation
                .Host.GetRequiredService<IConfiguration>()
                .GetAsync("mail", "password", cancellationToken);

            // Assert
            Assert.Equal("fictional-password", read);
            Assert.Single(workstation.Keyring.Writes);
            Assert.DoesNotContain(
                workstation.Log.Messages,
                message => message.Contains("created encryption key", StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    /// <summary>UC-007. A value stored in clear text becomes sealed in place; readers never notice.</summary>
    [Fact]
    public async Task MoveAsync_PlainEntry_EncryptsWithoutChangingValueAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync(
                "mail",
                "password",
                "fictional-password",
                cancellationToken: cancellationToken
            );
            var plainDocument = workstation.ReadDocument("mail");
            var moved = await configuration.MoveAsync("mail", "password", "aesLocal", cancellationToken);
            var read = await configuration.GetAsync("mail", "password", cancellationToken);
            // Already there: nothing rewritten
            await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.MoveAsync("mail", "password", "aesLocal", cancellationToken);
            });

            // Assert
            Assert.Contains("fictional-password", plainDocument, StringComparison.Ordinal);
            Assert.Equal(string.Empty, moved.From);
            Assert.Equal("aesLocal", moved.To);
            Assert.Equal("fictional-password", read);
            Assert.DoesNotContain("fictional-password", workstation.ReadDocument("mail"), StringComparison.Ordinal);
        }
    }

    /// <summary>UC-008. List a namespace, remove an entry, list again.</summary>
    [Fact]
    public async Task RemoveAsync_ExistingEntry_UpdatesListingAndThrowsOnReadAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "server", "smtp.example.test", cancellationToken: cancellationToken);
            await configuration.SetAsync("mail", "password", "fictional", "aesLocal", cancellationToken);
            var listed = await configuration.ListAsync("mail", cancellationToken);
            await configuration.RemoveAsync("mail", "password", cancellationToken);
            var listedAgain = await configuration.ListAsync("mail", cancellationToken);
            var gone = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await configuration.GetAsync("mail", "password", cancellationToken);
            });
            // An empty namespace lists as empty, not as a failure
            var empty = await configuration.ListAsync("nothing-here", cancellationToken);

            // Assert
            Assert.Equal(["password", "server"], listed);
            Assert.Equal(["server"], listedAgain);
            Assert.Equal(ErrorCategory.NotFound, Assert.Single(gone.Errors).Category);
            Assert.Empty(empty);
        }
    }

    /// <summary>UC-009. The same round-trip with no reflection: the app declares its JSON context once and
    /// passes the type info, which is the path the AOT analyzer guides it to.
    /// </summary>
    [Fact]
    public async Task SetAsAsync_SourceGeneratedJson_RoundTripsNestedSettingsAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var settings = new MailServerSettings
            {
                Host = "smtp.aot.example.test",
                Port = 465,
                Security = new MailSecuritySettings
                {
                    IsTlsEnabled = true,
                    Credentials = new MailCredentials
                    {
                        UserName = "tutorial@example.test",
                        Password = "fictional-tutorial-password",
                    },
                },
            };
            var typeInfo = AppJsonContext.Default.MailServerSettings;

            // Act
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            // Store the complete nested object as one "mail/server" entry using plain JSON.
            await workstation
                .Host.GetRequiredService<IConfiguration>()
                .SetAsAsync("mail", "server", settings, typeInfo, cancellationToken: cancellationToken);
            // A fresh caller scope reads the persisted value rather than relying on the original client.
            var stored = await workstation
                .RefreshedConfigurationService()
                .GetAsAsync("mail", "server", typeInfo, cancellationToken);

            // Assert
            Assert.Equal("smtp.aot.example.test", stored.Host);
            Assert.Equal(465, stored.Port);
            Assert.True(stored.Security.IsTlsEnabled);
            Assert.Equal("tutorial@example.test", stored.Security.Credentials.UserName);
            Assert.Equal("fictional-tutorial-password", stored.Security.Credentials.Password);
        }
    }
}
