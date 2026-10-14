using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Explicit removal requests provider deletion and reports any retained remote copy.</summary>
public sealed class ConfigurationRemovalTutorials
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemoveAsync_VaultValue_DeletesEntryAndReportsRetentionAsync(bool retainRemotely, bool wholeDto)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var vault = vaults.Created[0];
            vault.RetainOnRemoval = retainRemotely;
            await configuration.SetAsync("mail", "host", "smtp.example.test", cancellationToken: cancellationToken);
            if (wholeDto)
                await configuration.SetAsAsync(
                    "mail",
                    "secret",
                    new { Password = "fictional-secret", Port = 587 },
                    "mailVault",
                    cancellationToken
                );
            else
                await configuration.SetAsync("mail", "secret", "fictional-secret", "mailVault", cancellationToken);
            var removed = await configuration.RemoveAsync("mail", "secret", cancellationToken);
            var remainingKeys = await fixture.RefreshedConfigurationService().ListAsync("mail", cancellationToken);
            var readException = await Assert.ThrowsAsync<ResultException>(() =>
                configuration.GetAsync("mail", "secret", cancellationToken).AsTask()
            );
            // With the local pointer gone, a repeated removal does not contact the vault again.
            var repeated = await configuration.RemoveAsync("mail", "secret", cancellationToken);

            // Assert
            Assert.Equal(retainRemotely, removed.RemoteValueRemains);
            Assert.Equal(["host"], remainingKeys);
            Assert.Equal(ErrorCategory.NotFound, Assert.Single(readException.Errors).Category);
            Assert.Empty(vault.Values);
            Assert.Equal(retainRemotely, vault.RetainedValues.ContainsKey("mail/secret"));
            Assert.Equal(1, vault.Removals);
            Assert.Equal(0, vault.Reads);
            Assert.False(repeated.RemoteValueRemains);
        }
    }

    [Fact]
    public async Task RemoveAsync_VaultUnavailable_PreservesEntryForRetryAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var failure = Error.FromMessages("Vault unavailable.", "Connection failed.", ErrorCategory.Unavailable);

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "password", "fictional-secret", "mailVault", cancellationToken);
            var before = fixture.ReadDocument("mail");
            var vault = vaults.Created[0];
            vault.RemovalFailure = failure;
            var exception = await Assert.ThrowsAsync<ResultException>(() =>
                configuration.RemoveAsync("mail", "password", cancellationToken).AsTask()
            );
            var afterFailure = fixture.ReadDocument("mail");
            var remoteAfterFailure = vault.Values.GetValueOrDefault("mail/password");
            vault.RemovalFailure = null;
            var retried = await configuration.RemoveAsync("mail", "password", cancellationToken);
            var remainingKeys = await configuration.ListAsync("mail", cancellationToken);

            // Assert
            Assert.Same(failure, Assert.Single(exception.Errors));
            Assert.Equal(before, afterFailure);
            Assert.Equal("fictional-secret", remoteAfterFailure);
            Assert.False(retried.RemoteValueRemains);
            Assert.Empty(remainingKeys);
            Assert.Empty(vault.Values);
            Assert.Equal(2, vault.Removals);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveAsync_VaultValue_ReportsWhetherSourceCopyRemainsAsync(bool retainRemotely)
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var cancellationToken = TestContext.Current.CancellationToken;

            // Act
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "mailVault", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            var vault = vaults.Created[0];
            vault.RetainOnRemoval = retainRemotely;
            await configuration.SetAsync("mail", "password", "fictional-secret", "mailVault", cancellationToken);
            var moved = await configuration.MoveAsync("mail", "password", cancellationToken: cancellationToken);
            var value = await fixture.RefreshedConfigurationService().GetAsync("mail", "password", cancellationToken);

            // Assert
            Assert.Equal(!retainRemotely, moved.SourceWithdrawn);
            Assert.Equal("fictional-secret", value);
            Assert.Empty(vault.Values);
            Assert.Equal(retainRemotely, vault.RetainedValues.ContainsKey("mail/password"));
            Assert.Equal(1, vault.Removals);
        }
    }
}
