using System.Net;
using System.Text;
using System.Text.Json;
using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// The token endpoint of an OAuth2 authorization server, as an HttpMessageHandler the library's
/// HTTP client is pointed at. It issues a grant for an authorization code, rotates the grant on
/// refresh, and can be told to start rejecting refresh tokens the way a revoked one is rejected.
/// </summary>
public sealed class FakeAuthorizationServer : HttpMessageHandler
{
    private int _issued;

    public static Uri AuthorizationEndpoint { get; } = new("https://auth.example.test/oauth/authorize");

    public static Uri TokenEndpoint { get; } = new("https://auth.example.test/oauth/token");

    /// <summary>What an app that knows this server passes to AuthorizeAsync.</summary>
    public static OAuthOptions Options(params string[] scopes) => new()
    {
        AuthorizationEndpoint = AuthorizationEndpoint,
        TokenEndpoint = TokenEndpoint,
        ClientId = "someapp-cli",
        Scopes = scopes,
    };

    public int CodeExchanges { get; private set; }

    public int RefreshCalls { get; private set; }

    /// <summary>The access token most recently issued — what a bearer of the credential should present.</summary>
    public string? LatestAccessToken { get; private set; }

    /// <summary>When true, every refresh answers 400 invalid_grant, as after revocation.</summary>
    public bool RejectRefreshTokens { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != TokenEndpoint || request.Method != HttpMethod.Post || request.Content is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        var form = Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        lock (this)
        {
            switch (form.GetValueOrDefault("grant_type"))
            {
                case "authorization_code" when form.GetValueOrDefault("code") == FakeBrowser.AuthorizationCode
                                            && form.ContainsKey("code_verifier"):
                    CodeExchanges++;
                    return Grant();

                case "refresh_token" when form.GetValueOrDefault("refresh_token") == $"refresh-{_issued}":
                    RefreshCalls++;
                    return RejectRefreshTokens ? InvalidGrant() : Grant();

                default:
                    return InvalidGrant();
            }
        }
    }

    private HttpResponseMessage Grant()
    {
        _issued++;
        LatestAccessToken = $"access-{_issued}";
        return Json(HttpStatusCode.OK, new
        {
            access_token = LatestAccessToken,
            refresh_token = $"refresh-{_issued}",
            token_type = "Bearer",
            expires_in = 3600,
        });
    }

    private static HttpResponseMessage InvalidGrant() =>
        Json(HttpStatusCode.BadRequest, new { error = "invalid_grant", error_description = "The refresh token has been revoked." });

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private static Dictionary<string, string> Parse(string form) =>
        form.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
                StringComparer.Ordinal);
}
