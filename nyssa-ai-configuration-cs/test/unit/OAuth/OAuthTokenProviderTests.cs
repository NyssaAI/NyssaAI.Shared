using System.Net;
using System.Text;
using System.Text.Json;
using NyssaAI.Results;

using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Values;
using NyssaAI.Configuration.OAuth.Infrastructure;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.CloudKeyVaults;

namespace NyssaAI.Configuration.Tests.OAuth;

/// <summary>
/// The refresh and persistence semantics of the OAuth provider, driven through a fake token
/// endpoint. The interactive flows are covered end to end by the use case tests, which drive the
/// real loopback listener with a fake browser; nothing here opens one.
/// </summary>
public sealed class OAuthTokenProviderTests
{
    private static readonly OAuthOptions Server = new()
    {
        AuthorizationEndpoint = new Uri("https://auth.example.test/authorize"),
        TokenEndpoint = new Uri("https://auth.example.test/token"),
        ClientId = "tests",
    };

    [Fact]
    public async Task ReadAsync_WithConcurrentExpiredReaders_RefreshesOnceAndPersistsRotation()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var endpoint = new TokenEndpointStub(accessToken: "new-access", refreshToken: "new-refresh", expiresIn: 3600);
        using var client = new HttpClient(endpoint);
        var provider = new OAuthTokenProvider(cipher, documents, client, new NoBrowser(), clock, refreshSkew: TimeSpan.FromMinutes(5));
        var store = ConfigurationStore.Create(documents, [provider]).Value;
        var expired = JsonSerializer.Serialize(
            Credential("old-access", "old-refresh", clock.GetUtcNow().AddMinutes(1)),
            OAuthGrantJsonContext.Default.OAuthToken);
        Assert.True((await store.SetAsync("service", "token", expired, provider.Name, TestContext.Current.CancellationToken)).IsSuccess);

        var reads = await Task.WhenAll(
            store.GetAsync("service", "token", TestContext.Current.CancellationToken).AsTask(),
            store.GetAsync("service", "token", TestContext.Current.CancellationToken).AsTask());

        Assert.All(reads, read => Assert.Equal("new-access", read.Value));
        Assert.Equal(1, endpoint.Calls);
        Assert.Equal("old-refresh", endpoint.LastRefreshTokenPresented);

        // A second provider over the same documents finds the rotated grant fresh and never calls out.
        var mustNotRefresh = new TokenEndpointStub(accessToken: "unused", refreshToken: "unused", expiresIn: 3600);
        using var secondClient = new HttpClient(mustNotRefresh);
        var reopenedProvider = new OAuthTokenProvider(cipher, documents, secondClient, new NoBrowser(), clock, refreshSkew: TimeSpan.FromMinutes(5));
        var reopened = ConfigurationStore.Create(documents, [reopenedProvider]).Value;
        Assert.Equal("new-access", (await reopened.GetAsync("service", "token", TestContext.Current.CancellationToken)).Value);
        Assert.Equal(0, mustNotRefresh.Calls);
    }

    [Fact]
    public async Task ReadAsync_WhenTheServerRejectsTheRefreshToken_MarksTheEntryAndReportsForbidden()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var endpoint = new TokenEndpointStub(accessToken: "unused", refreshToken: "unused", expiresIn: 3600) { RejectRefresh = true };
        using var client = new HttpClient(endpoint);
        var provider = new OAuthTokenProvider(cipher, documents, client, new NoBrowser(), clock, refreshSkew: TimeSpan.FromMinutes(5));
        var store = ConfigurationStore.Create(documents, [provider]).Value;
        var cancellationToken = TestContext.Current.CancellationToken;
        var stale = JsonSerializer.Serialize(
            Credential("old-access", "old-refresh", clock.GetUtcNow().AddMinutes(1)),
            OAuthGrantJsonContext.Default.OAuthToken);
        Assert.True((await store.SetAsync("service", "token", stale, provider.Name, cancellationToken)).IsSuccess);

        var first = await store.GetAsync("service", "token", cancellationToken);
        var second = await store.GetAsync("service", "token", cancellationToken);

        Assert.True(first.IsFailure);
        Assert.Equal(ErrorCategory.Forbidden, first.Errors[0].Category);
        Assert.Contains("re-authorization required", first.Errors[0].TechnicalMessage, StringComparison.Ordinal);
        // The mark is persisted: the second read fails fast without presenting the token again.
        Assert.True(second.IsFailure);
        Assert.Equal(1, endpoint.Calls);
        var entries = await documents.Create("service").Value.ReadAsync(cancellationToken);
        Assert.True(entries.Value["token"].ReauthRequired);
    }

    [Fact]
    public async Task WriteAsync_WithNoRefreshToken_ReturnsValidation()
    {
        using var client = new HttpClient(new TokenEndpointStub("unused", "unused", 3600));
        var provider = new OAuthTokenProvider(
            ConfigurationCipherAesGcm.Create(new byte[32]).Value,
            new InMemoryDocumentFactory(),
            client,
            new NoBrowser());
        var payload = JsonSerializer.Serialize(
            Credential("access", "", DateTimeOffset.UtcNow.AddHours(1)),
            OAuthGrantJsonContext.Default.OAuthToken);

        var result = await provider.WriteAsync(payload, "service", "token", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReauthorizeAsync_WhenTheEntryHasNoReauthorizableProvider_ReturnsValidation(bool literal)
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryption = new ConfigurationProviderLocalEncryption(cipher);
        var store = ConfigurationStore.Create(documents, [encryption]).Value;
        Assert.True((await store.SetAsync("service", "token", "value", literal ? null : encryption.Name, cancellationToken)).IsSuccess);

        var reauthorized = await store.ReauthorizeAsync("service", "token", cancellationToken);

        Assert.True(reauthorized.IsFailure);
        Assert.Equal(ErrorCategory.Validation, reauthorized.Errors[0].Category);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenNoProviderCanAuthorize_ReturnsValidation()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path);
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var store = ConfigurationStore.Create(documents, [new ConfigurationProviderLocalEncryption(cipher)]).Value;

        var authorized = await store.AuthorizeAsync("service", "token", Server, TestContext.Current.CancellationToken);

        Assert.True(authorized.IsFailure);
        Assert.Equal(ErrorCategory.Validation, authorized.Errors[0].Category);
    }

    private static OAuthToken Credential(string accessToken, string refreshToken, DateTimeOffset expiresAt) =>
        OAuthToken.From(accessToken, refreshToken, "Bearer", expiresAt, Server);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveAsync_VaultCredentialInSameNamespace_ResolvesTokenAndPreservesCredential(bool expired)
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path, TimeSpan.FromMilliseconds(250));
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var endpoint = new TokenEndpointStub("new-access", "new-refresh", 3600);
        using var http = new HttpClient(endpoint);
        var oauth = new OAuthTokenProvider(cipher, documents, http, new NoBrowser(), clock);
        var reference = new StoreReference();
        var vault = new AuthenticatedRemovalVault(new AccessTokenSource("vault", reference));
        var store = ConfigurationStore.Create(documents, [oauth, new CloudKeyVaultProvider(vault)],
            [new ProviderCredential { Provider = "vault", Namespace = "service", Key = "token" }]).Value;
        reference.Current = store;
        var token = TestContext.Current.CancellationToken;
        var credential = JsonSerializer.Serialize(Credential("old-access", "old-refresh",
            clock.GetUtcNow().AddHours(expired ? -1 : 1)), OAuthGrantJsonContext.Default.OAuthToken);
        (await store.SetAsync("service", "token", credential, oauth.Name, token)).ThrowIfError();
        (await documents.Create("service").Value.ModifyAsync(entries =>
        {
            entries["password"] = new ConfigurationValue { Value = "secrets/1", Provider = "vault" };
            return ValueTask.FromResult(Result.Success());
        }, token)).ThrowIfError();

        var removed = (await store.RemoveAsync("service", "password", token)).ThrowIfError();

        Assert.False(removed.RemoteValueRemains);
        Assert.Equal("secrets/1", vault.RemovedPointer);
        Assert.Equal(expired ? "new-access" : "old-access", vault.PresentedToken);
        Assert.Equal(expired ? 1 : 0, endpoint.Calls);
        Assert.Equal(["token"], (await store.ListAsync("service", token)).ThrowIfError());
        Assert.Equal(expired ? "new-access" : "old-access", (await store.GetAsync("service", "token", token)).ThrowIfError());
        Assert.Equal(expired ? 1 : 0, endpoint.Calls);
    }

    [Fact]
    public async Task RemoveAsync_TargetReplacedWhileResolvingCredential_PreservesReplacementWithoutRemoteDeletion()
    {
        using var directory = new TemporaryDirectory();
        var documents = new ConfigurationDocumentFactoryLocalFile(directory.Path, TimeSpan.FromMilliseconds(250));
        var reference = new StoreReference();
        var vault = new AuthenticatedRemovalVault(new AccessTokenSource("vault", reference));
        var credential = new ReplacingCredentialProvider(documents);
        var store = ConfigurationStore.Create(documents, [credential, new CloudKeyVaultProvider(vault)],
            [new ProviderCredential { Provider = "vault", Namespace = "service", Key = "token" }]).Value;
        reference.Current = store;
        var token = TestContext.Current.CancellationToken;
        (await documents.Create("service").Value.ModifyAsync(entries =>
        {
            entries["password"] = new ConfigurationValue { Value = "secrets/original", Provider = "vault" };
            entries["token"] = new ConfigurationValue { Value = "access-token", Provider = credential.Name };
            return ValueTask.FromResult(Result.Success());
        }, token)).ThrowIfError();

        var removed = await store.RemoveAsync("service", "password", token);

        Assert.True(removed.IsFailure);
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(removed.Errors).Category);
        Assert.Null(vault.RemovedPointer);
        var entries = (await documents.Create("service").Value.ReadAsync(token)).ThrowIfError();
        Assert.Equal("secrets/replacement", entries["password"].Value);
    }

    [Fact]
    public async Task ReadAsync_NonrotatingRefreshPublicationFails_PersistsCompleteGrantWithoutAnotherExchange()
    {
        using var directory = new TemporaryDirectory();
        var documents = new FailingPublicationFactory(new ConfigurationDocumentFactoryLocalFile(directory.Path));
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var endpoint = new TokenEndpointStub("new-access", "same-refresh", 3600);
        using var http = new HttpClient(endpoint);
        var oauth = new OAuthTokenProvider(cipher, documents, http, new NoBrowser(), clock);
        var store = ConfigurationStore.Create(documents, [oauth]).Value;
        var token = TestContext.Current.CancellationToken;
        var credential = JsonSerializer.Serialize(Credential("old-access", "same-refresh",
            clock.GetUtcNow().AddHours(-1)), OAuthGrantJsonContext.Default.OAuthToken);
        (await store.SetAsync("service", "token", credential, oauth.Name, token)).ThrowIfError();
        documents.FailNext = true;

        Assert.Equal("new-access", (await store.GetAsync("service", "token", token)).ThrowIfError());

        var entry = (await documents.Create("service").Value.ReadAsync(token)).ThrowIfError()["token"];
        var persisted = JsonSerializer.Deserialize(cipher.Open(entry.Value, "service", "token").ThrowIfError(),
            OAuthGrantJsonContext.Default.OAuthToken)!;
        Assert.Equal("new-access", persisted.AccessToken);
        Assert.Equal("same-refresh", persisted.RefreshToken);
        Assert.Equal(clock.GetUtcNow().AddHours(1), persisted.ExpiresAt);
        Assert.False(entry.ReauthRequired);
        var reopened = ConfigurationStore.Create(documents,
            [new OAuthTokenProvider(cipher, documents, http, new NoBrowser(), clock)]).Value;
        Assert.Equal("new-access", (await reopened.GetAsync("service", "token", token)).ThrowIfError());
        Assert.Equal(1, endpoint.Calls);
    }

    [Fact]
    public async Task ReadAsync_HeldGrantSupersededByNonrotatingRefresh_PreservesNewerGrantAndDiscardsStaleRetry()
    {
        using var directory = new TemporaryDirectory();
        var documents = new FailingPublicationFactory(new ConfigurationDocumentFactoryLocalFile(directory.Path));
        var cipher = ConfigurationCipherAesGcm.Create(new byte[32]).Value;
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var firstEndpoint = new TokenEndpointStub("held-access", "same-refresh", 3600);
        var secondEndpoint = new TokenEndpointStub("newer-access", "same-refresh", 7200);
        using var firstHttp = new HttpClient(firstEndpoint);
        using var secondHttp = new HttpClient(secondEndpoint);
        var first = ConfigurationStore.Create(documents,
            [new OAuthTokenProvider(cipher, documents, firstHttp, new NoBrowser(), clock)]).Value;
        var second = ConfigurationStore.Create(documents,
            [new OAuthTokenProvider(cipher, documents, secondHttp, new NoBrowser(), clock)]).Value;
        var token = TestContext.Current.CancellationToken;
        var original = JsonSerializer.Serialize(Credential("expired-access", "same-refresh",
            clock.GetUtcNow().AddHours(-1)), OAuthGrantJsonContext.Default.OAuthToken);
        (await first.SetAsync("service", "token", original, "oauth", token)).ThrowIfError();
        documents.FailuresRemaining = 4;

        Assert.True((await first.GetAsync("service", "token", token)).IsFailure);
        Assert.Equal(0, documents.FailuresRemaining);
        Assert.Equal("newer-access", (await second.GetAsync("service", "token", token)).ThrowIfError());
        var newerEntry = (await documents.Create("service").Value.ReadAsync(token)).ThrowIfError()["token"];

        var retried = await first.GetAsync("service", "token", token);

        Assert.True(retried.IsFailure);
        Assert.Equal(ErrorCategory.Conflict, Assert.Single(retried.Errors).Category);
        Assert.Equal(newerEntry, (await documents.Create("service").Value.ReadAsync(token)).ThrowIfError()["token"]);
        Assert.Equal("newer-access", (await first.GetAsync("service", "token", token)).ThrowIfError());
        Assert.Equal(1, firstEndpoint.Calls);
        Assert.Equal(1, secondEndpoint.Calls);
    }

    private sealed class AuthenticatedRemovalVault(IAccessTokenSource tokens) : ICloudKeyVault
    {
        public string? PresentedToken { get; private set; }
        public string? RemovedPointer { get; private set; }

        public ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Removal must not read the secret.");

        public ValueTask<Result<ConfigurationPointer>> WriteAsync(string @namespace, string key, string value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The existing pointer is seeded directly.");

        public async ValueTask<Result<ConfigurationRemoval>> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
        {
            var access = await tokens.GetAccessTokenAsync(cancellationToken);
            if (access.IsFailure) return Result<ConfigurationRemoval>.Failure(access.Errors);
            PresentedToken = access.Value;
            RemovedPointer = pointer.Value;
            return Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false });
        }
    }

    private sealed class ReplacingCredentialProvider(IConfigurationDocumentFactory documents) : IConfigurationProvider
    {
        public string Name => "credential";

        public async ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default)
        {
            var changed = await documents.Create(@namespace).Value.ModifyAsync(entries =>
            {
                entries["password"] = new ConfigurationValue { Value = "secrets/replacement", Provider = "vault" };
                return ValueTask.FromResult(Result.Success());
            }, cancellationToken);
            return changed.IsFailure ? Result<string>.Failure(changed.Errors) : Result<string>.Success(entry.Value);
        }

        public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The credential is seeded directly.");

        public ValueTask<Result<ConfigurationRemoval>> EraseAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The credential must be preserved.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoBrowser : IBrowserLauncher
    {
        public ValueTask<Result> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No test here should open a browser.");
    }

    // Answers the token endpoint with one grant, or with invalid_grant, and records what it saw.
    private sealed class TokenEndpointStub(string accessToken, string refreshToken, int expiresIn) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        public string? LastRefreshTokenPresented { get; private set; }

        public bool RejectRefresh { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastRefreshTokenPresented = form.Split('&')
                .Select(pair => pair.Split('=', 2))
                .FirstOrDefault(pair => pair[0] == "refresh_token")?[1];

            if (RejectRefresh)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}","token_type":"Bearer","expires_in":{{expiresIn}}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class InMemoryDocumentFactory : IConfigurationDocumentFactory
    {
        public Result<IConfigurationDocument> Create(string @namespace) =>
            Result<IConfigurationDocument>.Failure(Error.FromMessages("Unused", "unused", ErrorCategory.Unexpected));
    }
}
