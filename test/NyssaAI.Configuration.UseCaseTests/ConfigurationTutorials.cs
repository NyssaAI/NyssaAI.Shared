using Microsoft.Extensions.DependencyInjection;
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
    public async Task SetAsAsync_BasicValue_RoundTripsAcrossScopesAsync<TValue>(TValue settingValue, TValue expectedValue)
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();

        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
        // SetAsAsync serializes the typed value into namespace "application", key "preference".
        // Plain is appropriate for these nonsecret preferences; secret values use the encryption provider.
        var saved = await configuration.SetAsAsync("application", "preference", settingValue, IConfiguration.Plain, cancellationToken);

        // A fresh caller scope reads the persisted value rather than relying on the original client.
        var stored = await workstation.RefreshedConfigurationService().GetAsAsync<TValue>("application", "preference", cancellationToken);

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.True(stored.IsSuccess, stored.ToString());
        Assert.Equal(expectedValue, stored.Value);
    }

    /// <summary>UC-004. A three-level object is one entry, restored intact.</summary>
    [Fact]
    public async Task SetAsAsync_NestedSettings_RestoresAllLevelsAsync()
    {
        // Arrange
        using var workstation = new Workstation();
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
        workstation.AddNyssaConfiguration();

        // Store the complete nested object as one "mail/server" entry using plain JSON.
        var saved = await workstation.Host.GetRequiredService<IConfiguration>()
            .SetAsAsync("mail", "server", settings, IConfiguration.Plain, cancellationToken);

        // A fresh caller scope reads the persisted value rather than relying on the original client.
        var stored = await workstation.RefreshedConfigurationService()
            .GetAsAsync<MailServerSettings>("mail", "server", cancellationToken);

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.True(stored.IsSuccess, stored.ToString());
        Assert.Equal("smtp.example.test", stored.Value.Host);
        Assert.Equal(587, stored.Value.Port);
        Assert.True(stored.Value.Security.IsTlsEnabled);
        Assert.Equal("tutorial@example.test", stored.Value.Security.Credentials.UserName);
        Assert.Equal("fictional-tutorial-password", stored.Value.Security.Credentials.Password);
    }

    /// <summary>UC-005. A secret is sealed by naming "aesLocal" once; an update keeps that provider without
    /// naming it; the app handles no key and constructs no cipher.
    /// </summary>
    [Fact]
    public async Task SetAsync_EncryptedEntry_InheritsProviderAndRejectsReplacementAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();

        // A brand-new key names its destination
        var saved = await configuration.SetAsync("mail", "password", "fictional-password-v1", provider: "aesLocal", cancellationToken: cancellationToken);

        // An update omits the provider and inherits the recorded one
        var updated = await configuration.SetAsync("mail", "password", "fictional-password-v2", cancellationToken: cancellationToken);

        var read = await configuration.GetAsync("mail", "password", cancellationToken);
        var document = workstation.ReadDocument("mail");

        // Naming a *different* provider on an existing entry is refused — that is what MoveAsync is for
        var elsewhere = await configuration.SetAsync("mail", "password", "v3", provider: IConfiguration.Plain, cancellationToken: cancellationToken);

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.True(updated.IsSuccess, updated.ToString());
        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal("fictional-password-v2", read.Value);
        Assert.DoesNotContain("fictional-password-v1", document, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-password-v2", document, StringComparison.Ordinal);
        Assert.Contains("\"aesLocal\"", document, StringComparison.Ordinal);
        Assert.True(elsewhere.IsFailure);
        Assert.Equal(ErrorCategory.Conflict, elsewhere.Errors[0].Category);
    }

    /// <summary>UC-006. A new process, same application name, opens what the previous process sealed — the key
    /// is in the credential store, not in either process.
    /// </summary>
    [Fact]
    public async Task GetAsync_RestartedApplication_ReusesEncryptionKeyAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        var saved = await workstation.Host.GetRequiredService<IConfiguration>().SetAsync(
            "mail", "password", "fictional-password", provider: "aesLocal", cancellationToken: cancellationToken);
        saved.ThrowIfError(); // A failed initial write cannot be used as a restart prerequisite.
        workstation.StopApplication();
        workstation.Log.Messages.Clear();

        // A second process: a new container on the same workstation
        workstation.AddNyssaConfiguration();
        var read = await workstation.Host.GetRequiredService<IConfiguration>().GetAsync("mail", "password", cancellationToken);

        // Assert
        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal("fictional-password", read.Value);
        Assert.Single(workstation.Keyring.Writes);
        Assert.DoesNotContain(workstation.Log.Messages, message => message.Contains("created encryption key", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>UC-007. A value stored in clear text becomes sealed in place; readers never notice.</summary>
    [Fact]
    public async Task MoveAsync_PlainEntry_EncryptsWithoutChangingValueAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();

        var saved = await configuration.SetAsync("mail", "password", "fictional-password", IConfiguration.Plain, cancellationToken);

        var plainDocument = workstation.ReadDocument("mail");
        var moved = await configuration.MoveAsync("mail", "password", "aesLocal", cancellationToken);

        var read = await configuration.GetAsync("mail", "password", cancellationToken);

        // Already there: nothing rewritten
        var again = await configuration.MoveAsync("mail", "password", "aesLocal", cancellationToken);

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.Contains("fictional-password", plainDocument, StringComparison.Ordinal);
        Assert.True(moved.IsSuccess, moved.ToString());
        Assert.Equal(IConfiguration.Plain, moved.Value.From);
        Assert.Equal("aesLocal", moved.Value.To);
        Assert.True(read.IsSuccess, read.ToString());
        Assert.Equal("fictional-password", read.Value);
        Assert.DoesNotContain("fictional-password", workstation.ReadDocument("mail"), StringComparison.Ordinal);
        Assert.True(again.IsFailure);
    }

    /// <summary>UC-008. List a namespace, remove an entry, list again.</summary>
    [Fact]
    public async Task RemoveAsync_ExistingEntry_UpdatesListingAndReturnsNotFoundAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();

        var savedServer = await configuration.SetAsync("mail", "server", "smtp.example.test", IConfiguration.Plain, cancellationToken);
        var savedPassword = await configuration.SetAsync("mail", "password", "fictional", "aesLocal", cancellationToken);

        var listed = await configuration.ListAsync("mail", cancellationToken);
        var removed = await configuration.RemoveAsync("mail", "password", cancellationToken);
        var listedAgain = await configuration.ListAsync("mail", cancellationToken);

        var gone = await configuration.GetAsync("mail", "password", cancellationToken);
        // An empty namespace lists as empty, not as a failure
        var empty = await configuration.ListAsync("nothing-here", cancellationToken);

        // Assert
        Assert.True(savedServer.IsSuccess, savedServer.ToString());
        Assert.True(savedPassword.IsSuccess, savedPassword.ToString());
        Assert.True(listed.IsSuccess, listed.ToString());
        Assert.Equal(["password", "server"], listed.Value);
        Assert.True(removed.IsSuccess, removed.ToString());
        Assert.True(listedAgain.IsSuccess, listedAgain.ToString());
        Assert.Equal(["server"], listedAgain.Value);
        Assert.Equal(ErrorCategory.NotFound, Assert.Single(gone.Errors).Category);
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value);
    }

    /// <summary>UC-009. The same round-trip with no reflection: the app declares its JSON context once and
    /// passes the type info, which is the path the AOT analyzer guides it to.
    /// </summary>
    [Fact]
    public async Task SetAsAsync_SourceGeneratedJson_RoundTripsNestedSettingsAsync()
    {
        // Arrange
        using var workstation = new Workstation();
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
        workstation.AddNyssaConfiguration();

        // Store the complete nested object as one "mail/server" entry using plain JSON.
        var saved = await workstation.Host.GetRequiredService<IConfiguration>()
            .SetAsAsync("mail", "server", settings, typeInfo, IConfiguration.Plain, cancellationToken);

        // A fresh caller scope reads the persisted value rather than relying on the original client.
        var stored = await workstation.RefreshedConfigurationService()
            .GetAsAsync("mail", "server", typeInfo, cancellationToken);

        // Assert
        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.True(stored.IsSuccess, stored.ToString());
        Assert.Equal("smtp.aot.example.test", stored.Value.Host);
        Assert.Equal(465, stored.Value.Port);
        Assert.True(stored.Value.Security.IsTlsEnabled);
        Assert.Equal("tutorial@example.test", stored.Value.Security.Credentials.UserName);
        Assert.Equal("fictional-tutorial-password", stored.Value.Security.Credentials.Password);
    }

}
