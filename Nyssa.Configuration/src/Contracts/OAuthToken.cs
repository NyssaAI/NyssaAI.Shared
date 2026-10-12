using System.Text.Json.Serialization;
using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// OAuthToken is one stored OAuth credential: the grant, and the authorization server it came
/// from. It is self-describing so that refreshing or re-acquiring it needs nothing from the app or
/// the manifest — one <c>oauth</c> provider serves every service the app talks to.
/// </summary>
/// <remarks>
/// The whole record is sealed by the local cipher before it reaches a document, so the server
/// description travels with the tokens and never in clear text.
/// </remarks>
internal sealed record OAuthToken
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required string TokenType { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required Uri AuthorizationEndpoint { get; init; }

    public required Uri TokenEndpoint { get; init; }

    public required string ClientId { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];

    public int? RedirectPort { get; init; }

    /// <summary>The server description, in the form the flows take.</summary>
    [JsonIgnore]
    public OAuthOptions Options => new()
    {
        AuthorizationEndpoint = AuthorizationEndpoint,
        TokenEndpoint = TokenEndpoint,
        ClientId = ClientId,
        Scopes = Scopes,
        RedirectPort = RedirectPort,
    };

    /// <summary>Builds the stored credential from a grant and the options it was obtained with.</summary>
    public static OAuthToken From(string accessToken, string refreshToken, string tokenType, DateTimeOffset expiresAt, OAuthOptions options) => new()
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        TokenType = tokenType,
        ExpiresAt = expiresAt,
        AuthorizationEndpoint = options.AuthorizationEndpoint,
        TokenEndpoint = options.TokenEndpoint,
        ClientId = options.ClientId,
        Scopes = options.Scopes,
        RedirectPort = options.RedirectPort,
    };

    public override string ToString() =>
        $"OAuthToken {{ TokenType = {TokenType}, ExpiresAt = {ExpiresAt:O}, TokenEndpoint = {TokenEndpoint}, "
        + "AccessToken = <redacted>, RefreshToken = <redacted> }";
}
