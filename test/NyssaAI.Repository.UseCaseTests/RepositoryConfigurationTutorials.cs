using NyssaAI.Repository.Configuration;
using NyssaAI.Repository.UseCaseTests.fixtures;
using Xunit;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.UseCaseTests;

public sealed class RepositoryConfigurationTutorials
{
    [Fact]
    public async Task Configure_ProtectedSettings_ReopensAndPreservesProtectionAsync()
    {
        // Arrange
        var directory = Path.Combine(Directory.GetCurrentDirectory(), ".temp", "f001-tutorial", Guid.NewGuid().ToString("N"));
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            using var fixture = new ConfigurationHost();
            ConfigurationStore configuration = fixture.Open(directory);

            var settings = new RepositoryConfiguration
            {
                DatabaseProvider = "PostgreSQL",
                ConnectionString = "Host=localhost;Password=sentinel-secret"
            };

            // Act
            // aesLocal encrypts the connection string using the startup keyring's key.
            var saved = await configuration.SetRepositoryConfigurationAsync(settings, storageProvider: "aesLocal", cancellationToken: cancellation);
            saved.ThrowIfError();

            // A new host reads the same manifest and credential store, like an application restart.
            ConfigurationStore reopened = fixture.Open(directory);
            var loaded = await reopened.GetRepositoryConfigurationAsync(cancellationToken: cancellation);

            var replacement = settings with { ConnectionString = "Host=localhost;Password=replacement-secret" };
            var updated = await reopened.SetRepositoryConfigurationAsync(replacement, cancellationToken: cancellation);
            updated.ThrowIfError();

            // Omitting the destination preserves encryption; Set cannot silently downgrade it.
            var wrongDestination = await reopened.SetRepositoryConfigurationAsync(replacement, storageProvider: ConfigurationStore.Plain, cancellationToken: cancellation);
            var protectedValue = await reopened.GetRepositoryConfigurationAsync(cancellationToken: cancellation);

            var storedDocuments = new List<string>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
                storedDocuments.Add(await File.ReadAllTextAsync(file, cancellation));

            // Another credential store has a different key and cannot decrypt the saved settings.
            ConfigurationStore differentKey = fixture.Open(directory, differentKey: true);
            var inaccessible = await differentKey.GetRepositoryConfigurationAsync(cancellationToken: cancellation);

            // Assert
            Assert.True(saved.IsSuccess, saved.ToString());
            Assert.True(loaded.IsSuccess, loaded.ToString());
            Assert.Equal("postgresql", loaded.Value.DatabaseProvider);
            Assert.Equal("Host=localhost;Password=sentinel-secret", loaded.Value.ConnectionString);
            Assert.True(updated.IsSuccess, updated.ToString());
            Assert.True(wrongDestination.IsFailure);
            Assert.True(protectedValue.IsSuccess, protectedValue.ToString());
            Assert.Equal("Host=localhost;Password=replacement-secret", protectedValue.Value.ConnectionString);
            Assert.All(storedDocuments, contents =>
            {
                Assert.DoesNotContain("sentinel-secret", contents, StringComparison.Ordinal);
                Assert.DoesNotContain("replacement-secret", contents, StringComparison.Ordinal);
            });
            Assert.True(inaccessible.IsFailure);
            Assert.NotEmpty(inaccessible.Errors);
            Assert.All(inaccessible.Errors, error =>
            {
                Assert.DoesNotContain("replacement-secret", error.UserMessage, StringComparison.Ordinal);
                Assert.DoesNotContain("replacement-secret", error.TechnicalMessage, StringComparison.Ordinal);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
