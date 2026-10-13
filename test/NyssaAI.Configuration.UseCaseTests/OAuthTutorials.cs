using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>
/// OAuth as a credential type. UC-012 through UC-016: acquire a credential, have it refreshed
/// without asking, recover when it is revoked, and let a vault package authenticate through one.
/// The browser and the authorization server are fakes; the loopback listener, PKCE, sealing,
/// locking, and refresh are the real code.
/// </summary>
public sealed class OAuthTutorials
{

    /// <summary>UC-012. The app's auth mail command acquires a credential for a service it talks to. The
    /// library opens the browser once, exchanges the code, and seals a self-describing credential under oauth.
    /// </summary>
    [Fact]
    public async Task AuthorizeAsync_ConsentGranted_StoresSealedCredentialAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        var extensions = workstation.Host.GetRequiredService<IConfigurationExtensions>();
        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();

        // auth mail: the app knows the mail service's authorization server; a browser is acceptable now
        var authorized = await extensions.AuthorizeAsync("mail", "token", FakeAuthorizationServer.Options("mail.read"), cancellationToken);

        var browserLaunchesBeforeRecovery = workstation.Browser.Launches;
        var codeExchangesAfterAuthorization = workstation.AuthorizationServer.CodeExchanges;
        // A read returns the access token the server issued
        var token = await configuration.GetAsync("mail", "token", cancellationToken);
        // and the entry on disk is sealed under oauth, with nothing in clear text
        var document = workstation.ReadDocument("mail");

        // Assert
        Assert.True(authorized.IsSuccess, authorized.ToString());
        Assert.Equal(1, browserLaunchesBeforeRecovery);
        Assert.Equal(1, codeExchangesAfterAuthorization);
        Assert.Equal("access-1", token);
        Assert.Contains("\"oauth\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain(token, document, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-", document, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeAuthorizationServer.TokenEndpoint.ToString(), document, StringComparison.Ordinal);
    }

    /// <summary>UC-012, failure case. The user declines. Nothing is stored, and the app gets the server's own reason.</summary>
    [Fact]
    public async Task AuthorizeAsync_ConsentDenied_StoresNothingAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;
        workstation.Browser.DenyConsent = true;

        // Act
        workstation.AddNyssaConfiguration();

        var authorized = await workstation.Host.GetRequiredService<IConfigurationExtensions>()
            .AuthorizeAsync("mail", "token", FakeAuthorizationServer.Options("mail.read"), cancellationToken);

        var codeExchangesAfterAuthorization = workstation.AuthorizationServer.CodeExchanges;
        var missing = await Assert.ThrowsAsync<ResultException>(async () =>
        {
            await workstation.Host.GetRequiredService<IConfiguration>().GetAsync("mail", "token", cancellationToken);
        });

        // Assert
        Assert.True(authorized.IsFailure);
        Assert.Contains("The user said no.", authorized.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Equal(0, codeExchangesAfterAuthorization);
        Assert.Equal(ErrorCategory.NotFound, Assert.Single(missing.Errors).Category);
    }

    /// <summary>UC-013. Once the token is inside the skew window an ordinary read refreshes it, and several
    /// concurrent reads cause exactly one refresh. No browser, no app involvement.
    /// </summary>
    [Fact]
    public async Task GetAsync_ConcurrentStaleTokenReaders_RefreshesOnceAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        var authorized = await workstation.Host.GetRequiredService<IConfigurationExtensions>().AuthorizeAsync(
            "mail", "token", FakeAuthorizationServer.Options("mail.read"), cancellationToken);
        authorized.ThrowIfError(); // Reading or refreshing requires a successfully acquired credential.

        // A read while the token is fresh makes no call at all
        var fresh = await workstation.Host.GetRequiredService<IConfiguration>().GetAsync("mail", "token", cancellationToken);
        var refreshCallsWhileFresh = workstation.AuthorizationServer.RefreshCalls;

        // 57 minutes on: three left of a one-hour token, inside the skew (see Skew)
        // A one-hour token now has three minutes left, inside the five-minute refresh window.
        workstation.Clock.Advance(TimeSpan.FromMinutes(57));
        var pendingReads = new List<Task<string>>();
        for (var reader = 0; reader < 5; reader++)
        {
            pendingReads.Add(workstation.Host.GetRequiredService<IConfiguration>().GetAsync("mail", "token", cancellationToken).AsTask());
        }
        var reads = await Task.WhenAll(pendingReads);

        var refreshCallsAfterExpiry = workstation.AuthorizationServer.RefreshCalls;
        var browserLaunchesAfterAuthorization = workstation.Browser.Launches;

        // Assert
        Assert.Equal("access-1", fresh);
        Assert.Equal(0, refreshCallsWhileFresh);
        Assert.All(reads, read => Assert.Equal("access-2", read));
        Assert.Equal(1, refreshCallsAfterExpiry);   // five readers, one round trip
        Assert.Equal(1, browserLaunchesAfterAuthorization);                     // unchanged since acquisition
    }

    /// <summary>UC-014. The server has revoked the refresh token. The read throws and opens no
    /// browser; the app's auth mail command recovers with ReauthorizeAsync, which needs no options because the
    /// entry already knows its server.
    /// </summary>
    [Fact]
    public async Task ReauthorizeAsync_RevokedRefreshToken_RecoversCredentialAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        workstation.AddNyssaConfiguration();
        var authorized = await workstation.Host.GetRequiredService<IConfigurationExtensions>().AuthorizeAsync(
            "mail", "token", FakeAuthorizationServer.Options("mail.read"), cancellationToken);
        authorized.ThrowIfError(); // Reading or refreshing requires a successfully acquired credential.
        // A one-hour token now has three minutes left, inside the five-minute refresh window.
        workstation.Clock.Advance(TimeSpan.FromMinutes(57));
        workstation.AuthorizationServer.RejectRefreshTokens = true;

        // A non-interactive command hits the dead credential
        var rejected = await Assert.ThrowsAsync<ResultException>(async () =>
        {
            await workstation.Host.GetRequiredService<IConfiguration>().GetAsync("mail", "token", cancellationToken);
        });

        var browserLaunchesBeforeRecovery = workstation.Browser.Launches;

        // Recovery: the user runs auth mail again
        workstation.AuthorizationServer.RejectRefreshTokens = false;
        var recovered = await workstation.Host.GetRequiredService<IConfigurationExtensions>()
            .ReauthorizeAsync("mail", "token", cancellationToken);
        var browserLaunchesAfterRecovery = workstation.Browser.Launches;
        var codeExchangesAfterRecovery = workstation.AuthorizationServer.CodeExchanges;
        var token = await workstation.Host.GetRequiredService<IConfiguration>().GetAsync("mail", "token", cancellationToken);

        // Assert
        Assert.Contains("re-authorization required", rejected.Errors[0].TechnicalMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mail/token", rejected.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Equal(1, browserLaunchesBeforeRecovery);   // Get opened nothing
        Assert.True(recovered.IsSuccess, recovered.ToString());
        Assert.Equal(2, browserLaunchesAfterRecovery);
        Assert.Equal(2, codeExchangesAfterRecovery);
        Assert.Equal("access-2", token);
    }

    /// <summary>UC-015. A vault that signs its requests with OAuth2 declares its authorization server and takes a
    /// token source; the manifest names the entry it authenticates with. Acquisition, storage, refresh, and
    /// recovery all happen in this library. The vault package never sees a refresh token.
    /// </summary>
    [Fact]
    public async Task AuthorizeProviderAsync_MissingCredential_EnablesVaultAndRefreshAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;
        workstation.ProvisionKey();
        workstation.VaultPackage = new FakeVaultFactory(FakeAuthorizationServer.Options("secrets.read", "secrets.write"));
        workstation.WriteManifest("""
            {
              "version": 1,
              "applications": {
                "SomeApp-cli": {
                  "configurationDirectory": "SomeApp-cli",
                  "keyVault": {
                    "name": "contoso",
                    "kind": "fake",
                    "endpoint": "https://vault.contoso.example",
                    "credential": "vault/contoso-token"
                  }
                }
              }
            }
            """);

        // Act
        workstation.AddNyssaConfiguration();
        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();
        var extensions = workstation.Host.GetRequiredService<IConfigurationExtensions>();

        var vault = workstation.VaultPackage.Created.Single();

        var unauthorized = await Assert.ThrowsAsync<ResultException>(async () =>
        {
            await configuration.SetAsync("mail", "password", "fictional", provider: "contoso", cancellationToken: cancellationToken);
        });
        var browserLaunchesBeforeRecovery = workstation.Browser.Launches;
        var vaultWritesBeforeAuthorization = vault.Writes;

        // Interactive sign-in stores the vault credential locally; expected authorization failure stays a Result.
        var signedIn = await extensions.AuthorizeProviderAsync("contoso", cancellationToken);
        var browserLaunchesAfterAuthorization = workstation.Browser.Launches;

        await configuration.SetAsync("mail", "password", "fictional", provider: "contoso", cancellationToken: cancellationToken);
        var read = await configuration.GetAsync("mail", "password", cancellationToken);

        // A one-hour token now has three minutes left, inside the five-minute refresh window.
        workstation.Clock.Advance(TimeSpan.FromMinutes(57));
        var later = await configuration.GetAsync("mail", "password", cancellationToken);

        var refreshCallsAfterExpiry = workstation.AuthorizationServer.RefreshCalls;
        var browserLaunchesAfterRefresh = workstation.Browser.Launches;

        // Assert
        Assert.Contains("authorization required", unauthorized.Errors[0].TechnicalMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contoso", unauthorized.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Equal(0, browserLaunchesBeforeRecovery);
        Assert.Equal(0, vaultWritesBeforeAuthorization);
        Assert.True(signedIn.IsSuccess, signedIn.ToString());
        Assert.Equal(1, browserLaunchesAfterAuthorization);
        Assert.Contains("\"oauth\"", workstation.ReadDocument("vault"), StringComparison.Ordinal);
        Assert.Equal("fictional", read);
        Assert.Contains("access-1", vault.BearerTokensSeen);
        Assert.Equal("fictional", later);
        Assert.Contains("access-2", vault.BearerTokensSeen);
        Assert.Equal(1, refreshCallsAfterExpiry);
        Assert.Equal(1, browserLaunchesAfterRefresh);
    }

    /// <summary>UC-016. The negative control for UC-015: a credential that would have to be read from the vault in
    /// order to open the vault is refused by name, before anything reaches the vault.
    /// </summary>
    [Fact]
    public async Task SetAsync_CircularVaultCredential_RejectsBeforeVaultAccessAsync()
    {
        // Arrange
        using var workstation = new Workstation();
        var cancellationToken = TestContext.Current.CancellationToken;
        workstation.ProvisionKey();
        workstation.VaultPackage = new FakeVaultFactory(FakeAuthorizationServer.Options("secrets.read"));
        workstation.WriteManifest("""
            {
              "version": 1,
              "applications": {
                "SomeApp-cli": {
                  "configurationDirectory": "SomeApp-cli",
                  "keyVault": { "name": "contoso", "kind": "fake", "credential": "vault/contoso-token" }
                }
              }
            }
            """);

        // Act
        workstation.AddNyssaConfiguration();
        IConfiguration configuration = workstation.Host.GetRequiredService<IConfiguration>();

        // Try to put the vault's own credential inside the vault
        var vault = workstation.VaultPackage.Created.Single();

        var circular = await Assert.ThrowsAsync<ResultException>(async () =>
        {
            await configuration.SetAsync("vault", "contoso-token", "anything", provider: "contoso", cancellationToken: cancellationToken);
        });

        var vaultWritesAfterRejection = vault.Writes;
        var browserLaunchesAfterRecovery = workstation.Browser.Launches;

        // Assert
        Assert.Contains("circular", circular.Errors[0].TechnicalMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contoso", circular.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains("vault/contoso-token", circular.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        Assert.Equal(0, vaultWritesAfterRejection);
        Assert.Equal(0, vault.Reads);
        Assert.Equal(0, browserLaunchesAfterRecovery);
    }

}
