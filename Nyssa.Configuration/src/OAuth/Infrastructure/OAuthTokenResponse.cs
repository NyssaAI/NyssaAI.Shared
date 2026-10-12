namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>The wire shape of a token-endpoint response, success or error, per RFC 6749 sections 5.1 and 5.2.</summary>
internal sealed record OAuthTokenResponse
{
    /// <summary>Gets the access token, absent on an error response.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Gets the token type, conventionally <c>Bearer</c>.</summary>
    public string? TokenType { get; init; }

    /// <summary>Gets the access token lifetime in seconds, when the server states one.</summary>
    public long? ExpiresIn { get; init; }

    /// <summary>Gets the refresh token, when the server issued or rotated one.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Gets the granted scope, when the server reports it.</summary>
    public string? Scope { get; init; }

    /// <summary>Gets the error code on an error response, such as <c>invalid_grant</c>.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the human-readable error description on an error response.</summary>
    public string? ErrorDescription { get; init; }

    /// <summary>Renders the response without its secrets.</summary>
    /// <remarks>
    /// This is the shape a token endpoint returns, so it carries the tokens before they reach
    /// <see cref="NyssaAI.Configuration.Contracts.OAuthToken"/>. It redacts for the same reason that record does.
    /// </remarks>
    public override string ToString() =>
        $"OAuthTokenResponse {{ TokenType = {TokenType}, ExpiresIn = {ExpiresIn}, "
        + $"Scope = {Scope}, Error = {Error}, ErrorDescription = {ErrorDescription}, "
        + "AccessToken = <redacted>, RefreshToken = <redacted> }";
}
