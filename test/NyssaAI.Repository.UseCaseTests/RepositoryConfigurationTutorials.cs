using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryConfigurationTutorials
{
    [Fact]
    public async Task Configure_ProtectedSettings_ReopensAndPreservesProtectionAsync()
    {
        // Arrange
        var directory = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".temp",
            "f001-tutorial",
            Guid.NewGuid().ToString("N")
        );
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            using (var fixture = new ConfigurationHost())
            {
                ConfigurationStore configuration = fixture.Open(directory);
                var settings = new RepositoryConfiguration
                {
                    DatabaseProvider = "PostgreSQL",
                    ConnectionString = "Host=localhost;Password=sentinel-secret",
                };

                // Act
                // aesLocal encrypts the connection string using the startup keyring's key.
                await configuration.SetRepositoryConfigurationAsync(
                    settings,
                    storageProvider: "aesLocal",
                    cancellationToken: cancellation
                );
                // A new host reads the same manifest and credential store, like an application restart.
                ConfigurationStore reopened = fixture.Open(directory);
                var loaded = await reopened.GetRepositoryConfigurationAsync(cancellationToken: cancellation);
                var replacement = settings with { ConnectionString = "Host=localhost;Password=replacement-secret" };
                await reopened.SetRepositoryConfigurationAsync(replacement, cancellationToken: cancellation);
                // Omitting the destination preserves encryption; Set cannot silently downgrade it.
                await Assert.ThrowsAsync<ResultException>(() =>
                    reopened.SetRepositoryConfigurationAsync(
                        replacement,
                        storageProvider: "otherProvider",
                        cancellationToken: cancellation
                    )
                );
                var protectedValue = await reopened.GetRepositoryConfigurationAsync(cancellationToken: cancellation);
                var storedDocuments = new List<string>();
                foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
                    storedDocuments.Add(await File.ReadAllTextAsync(file, cancellation));
                // Another credential store has a different key and cannot decrypt the saved settings.
                ConfigurationStore differentKey = fixture.Open(directory, differentKey: true);
                var inaccessible = await Assert.ThrowsAsync<ResultException>(() =>
                    differentKey.GetRepositoryConfigurationAsync(cancellationToken: cancellation)
                );

                // Assert
                Assert.Equal("postgresql", loaded.DatabaseProvider);
                Assert.Equal("Host=localhost;Password=sentinel-secret", loaded.ConnectionString);
                Assert.Equal("Host=localhost;Password=replacement-secret", protectedValue.ConnectionString);
                Assert.All(
                    storedDocuments,
                    contents =>
                    {
                        Assert.DoesNotContain("sentinel-secret", contents, StringComparison.Ordinal);
                        Assert.DoesNotContain("replacement-secret", contents, StringComparison.Ordinal);
                    }
                );
                Assert.NotEmpty(inaccessible.Errors);
                Assert.All(
                    inaccessible.Errors,
                    error =>
                    {
                        Assert.DoesNotContain("replacement-secret", error.UserMessage, StringComparison.Ordinal);
                        Assert.DoesNotContain("replacement-secret", error.TechnicalMessage, StringComparison.Ordinal);
                    }
                );
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
