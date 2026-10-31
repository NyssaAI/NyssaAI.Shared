using NyssaAI.Results;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// OAuthOptions is the caller-supplied description of one authorization server and public
/// client. It arrives from the caller rather than the configuration store, because the OAuth
/// types exist to serve the store and reading their own settings through it would be circular.
/// </summary>
/// <remarks>
/// Both endpoints must be <c>https</c>. The authorize URL is assembled from these values and
/// handed to a shell-executing browser launch, and the token endpoint receives the refresh token
/// in clear text, so neither may be plain HTTP. The flow is PKCE for a public client; there is no
/// client secret.
/// </remarks>
public sealed record OAuthOptions
{
    /// <summary>Gets the authorization endpoint the browser is sent to.</summary>
    public required Uri AuthorizationEndpoint { get; init; }

    /// <summary>Gets the token endpoint codes and refresh tokens are exchanged at.</summary>
    public required Uri TokenEndpoint { get; init; }

    /// <summary>Gets the public client identifier.</summary>
    public required string ClientId { get; init; }

    /// <summary>Gets the scopes requested at authorization, space-joined on the wire.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// Gets the loopback port the redirect listener binds, for a provider that requires a
    /// pre-registered port. An ephemeral port is chosen when null, per RFC 8252.
    /// </summary>
    public int? RedirectPort { get; init; }

    /// <summary>Checks that the endpoints are absolute <c>https</c> URIs and the client id is present.</summary>
    internal Result Validate()
    {
        if (!IsHttps(AuthorizationEndpoint))
            return Result.Failure(EndpointNotHttps("authorization", AuthorizationEndpoint));

        if (!IsHttps(TokenEndpoint))
            return Result.Failure(EndpointNotHttps("token", TokenEndpoint));

        if (string.IsNullOrWhiteSpace(ClientId))
            return Result.Failure(ClientIdRequired);

        if (RedirectPort is { } port && port is < 1 or > 65535)
            return Result.Failure(RedirectPortInvalid(port));

        return Result.Success();
    }

    /// <summary>The failure returned when an endpoint is not an absolute <c>https</c> URI.</summary>
    /// <param name="name">Which endpoint was refused.</param>
    /// <param name="endpoint">The endpoint that was supplied.</param>
    internal static Error EndpointNotHttps(string name, Uri? endpoint) => Error.FromMessages(
        "The sign-in configuration is not valid.",
        $"oauth: the {name} endpoint must be an absolute https URI; '{endpoint}' was supplied",
        ErrorCategory.Validation);

    /// <summary>The failure returned when no client id was supplied.</summary>
    internal static Error ClientIdRequired { get; } = Error.FromMessages(
        "The sign-in configuration is not valid.",
        "oauth: a client id is required",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the redirect port is outside the valid range.</summary>
    /// <param name="port">The port that was supplied.</param>
    internal static Error RedirectPortInvalid(int port) => Error.FromMessages(
        "The sign-in configuration is not valid.",
        $"oauth: redirect port {port} is not between 1 and 65535",
        ErrorCategory.Validation);

    private static bool IsHttps(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
