using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Shows that fixture configuration never writes to the developer's own manifest and isolates user keychains.</summary>
public sealed class ConfigurationIsolationTests
{
    [Fact]
    public async Task AddNyssaConfiguration_IsolatedWorkstation_LeavesUserManifestUnchangedAsync()
    {
        // Arrange
        using (var workstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var userManifest = BootstrapOptions.DefaultConfigManifest;
            // Compare fingerprints so a failed isolation check cannot print the user's provider secrets.
            var manifestBefore = File.Exists(userManifest)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(userManifest)))
                : null;

            // Act
            // The fixture supplies its private manifest and settings directory to the public registration.
            var services = workstation.CreateServices();
            services.AddNyssaConfiguration(workstation.Locate);
            workstation.StartApplication(services);
            IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
            // Store an ordinary label without encryption, then read it through the same caller interface.
            await configuration.SetAsync(
                "tutorial",
                "name",
                "isolated-workstation",
                cancellationToken: cancellationToken
            );
            var name = await configuration.GetAsync("tutorial", "name", cancellationToken);
            var manifestAfter = File.Exists(userManifest)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(userManifest)))
                : null;

            // Assert
            Assert.Equal("isolated-workstation", name);
            Assert.Equal(manifestBefore, manifestAfter);
            Assert.NotEqual(userManifest, workstation.ManifestPath);
            Assert.True(File.Exists(workstation.ManifestPath));
            Assert.True(workstation.DocumentExists("tutorial"));
        }
    }

    /// <summary>F-003 UC-005. A different user account has its own isolated OS keychain and cannot retrieve
    /// the original user's Master Encryption Key or decrypt a copied namespace JSON file.
    /// </summary>
    [Fact]
    public async Task GetAsync_DifferentUserAccountWithCopiedEncryptedFile_CannotRetrieveKeyOrDecryptSecretAsync()
    {
        // Arrange
        using (var aliceWorkstation = new Workstation())
        using (var bobWorkstation = new Workstation())
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            var aliceServices = aliceWorkstation.CreateServices();
            aliceServices.AddNyssaConfiguration(aliceWorkstation.Locate);
            aliceWorkstation.StartApplication(aliceServices);
            var aliceConfig = aliceWorkstation.Host.GetRequiredService<IConfiguration>();

            await aliceConfig.SetAsync(
                "mail",
                "password",
                "alice-top-secret-password",
                provider: "aesLocal",
                cancellationToken: cancellationToken
            );

            // Verify Bob's keychain cannot see Alice's master key before Bob initializes his own app
            var bobKeyringLookup = bobWorkstation.Keyring.Get(Workstation.ApplicationName, "configuration-key");
            Assert.True(bobKeyringLookup.IsSuccess);
            Assert.Null(bobKeyringLookup.Value);

            // Bob starts the app on his account (generating Bob's own distinct Master Encryption Key)
            var bobServices = bobWorkstation.CreateServices();
            bobServices.AddNyssaConfiguration(bobWorkstation.Locate);
            bobWorkstation.StartApplication(bobServices);
            var bobConfig = bobWorkstation.Host.GetRequiredService<IConfiguration>();

            // Bob copies Alice's encrypted mail.json into his own configuration directory
            Directory.CreateDirectory(bobWorkstation.ConfigurationDirectory);
            File.WriteAllText(
                Path.Combine(bobWorkstation.ConfigurationDirectory, "mail.json"),
                aliceWorkstation.ReadDocument("mail")
            );

            // Act
            var failure = await Assert.ThrowsAsync<ResultException>(async () =>
            {
                await bobConfig.GetAsync("mail", "password", cancellationToken);
            });

            // Assert
            Assert.NotEqual(
                aliceWorkstation.Keyring.Get(Workstation.ApplicationName, "configuration-key").Value,
                bobWorkstation.Keyring.Get(Workstation.ApplicationName, "configuration-key").Value
            );
            Assert.Single(failure.Errors);
            Assert.DoesNotContain(
                "alice-top-secret-password",
                failure.Errors[0].TechnicalMessage,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("alice-top-secret-password", failure.Errors[0].UserMessage, StringComparison.Ordinal);
        }
    }
}
