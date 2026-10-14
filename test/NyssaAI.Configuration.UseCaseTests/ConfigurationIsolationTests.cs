using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Shows that fixture configuration never writes to the developer's own manifest.</summary>
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
}
