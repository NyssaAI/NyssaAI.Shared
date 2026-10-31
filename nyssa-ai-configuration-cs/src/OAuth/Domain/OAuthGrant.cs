namespace NyssaAI.Configuration.OAuth.Domain;

/// <summary>
/// OAuthGrant is what the token endpoint returned from one exchange: the access token, the
/// refresh token when the server sent one, the token type, and the absolute expiry derived from
/// the relative <c>expires_in</c> and the clock at the time of the exchange.
/// </summary>
/// <remarks>
/// Unlike <c>OAuthToken</c>, the refresh token is optional here. A refresh exchange that
/// omits it means "keep the one you presented"; an authorization exchange that omits it is
/// refused, because a grant with no refresh token cannot outlive its access token.
/// </remarks>
internal sealed record OAuthGrant
{
    /// <summary>Gets the token presented to the resource server.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Gets the refresh token the server issued, or null when it issued none.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Gets the token type, conventionally <c>Bearer</c>.</summary>
    public required string TokenType { get; init; }

    /// <summary>Gets the instant at which <see cref="AccessToken"/> expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Renders the record without its secrets.</summary>
    /// <remarks>
    /// The synthesized ToString of a record prints every property, so one interpolation or one
    /// structured-logging call would put the tokens in the log. Only the non-secret fields render.
    /// </remarks>
    public override string ToString() =>
        $"OAuthGrant {{ TokenType = {TokenType}, ExpiresAt = {ExpiresAt:O}, "
        + "AccessToken = <redacted>, RefreshToken = <redacted> }";

}
