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
        Assert.True((await store.SetAsync("service", "token", "value", literal ? IConfiguration.Plain : encryption.Name, cancellationToken)).IsSuccess);

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
