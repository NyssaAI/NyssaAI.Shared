using System.Text;
using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.OAuth.Domain;
using NyssaAI.Configuration.Contracts;

using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// OAuthAuthorizationClientLoopback runs the interactive authorization-code flow for a native
/// app (RFC 8252): it binds a loopback listener, opens the browser at the authorization endpoint
/// with a PKCE challenge and a <c>state</c>, waits for the callback, validates the state, and
/// exchanges the code for a grant. A grant without a refresh token is refused.
/// </summary>
/// <remarks>
/// One instance runs one flow against one server. The provider builds it per authorization from
/// the options the app supplied, or from the server the existing entry already records.
/// </remarks>
internal sealed class OAuthAuthorizationClientLoopback
{
    private readonly OAuthOptions _options;
    private readonly OAuthTokenEndpoint _endpoint;
    private readonly IBrowserLauncher _browser;
    private readonly ILogger? _logger;

    private OAuthAuthorizationClientLoopback(OAuthOptions options, OAuthTokenEndpoint endpoint, IBrowserLauncher browser, ILogger? logger)
    {
        _options = options;
        _endpoint = endpoint;
        _browser = browser;
        _logger = logger;
    }

    /// <summary>Creates a client for one flow, failing when the options are not valid.</summary>
    /// <param name="options">The authorization server and client.</param>
    /// <param name="client">The HTTP client the code exchange goes through; borrowed.</param>
    /// <param name="browser">What opens the authorize URI: the user's browser, or a test's stand-in.</param>
    /// <param name="clock">The clock expiry is computed against; the system clock when null.</param>
    /// <param name="logger">The logger flow outcomes are reported to, if any.</param>
    public static Result<OAuthAuthorizationClientLoopback> Create(
        OAuthOptions options,
        HttpClient client,
        IBrowserLauncher browser,
        TimeProvider? clock = null,
        ILogger? logger = null)
    {
        var endpoint = OAuthTokenEndpoint.Create(options, client, clock, logger: logger);
        return endpoint.IsFailure
            ? Result<OAuthAuthorizationClientLoopback>.Failure(endpoint.Errors)
            : Result<OAuthAuthorizationClientLoopback>.Success(
                new OAuthAuthorizationClientLoopback(options, endpoint.Value, browser, logger));
    }

    /// <summary>Runs the flow and returns the credential it produced, describing the server it came from.</summary>
    public async ValueTask<Result<OAuthToken>> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        var listener = LoopbackCallbackListener.Create(_options.RedirectPort);
        if (listener.IsFailure)
            return Result<OAuthToken>.Failure(listener.Errors);

        // Disposed on every path, so the port is released whether the flow completes, fails, or
        // is cancelled.
        using (listener.Value)
        {
            var pkce = PkceChallenge.Create();
            var authorizeUri = BuildAuthorizeUri(listener.Value.RedirectUri, pkce);

            var launched = await _browser.LaunchAsync(authorizeUri, cancellationToken).ConfigureAwait(false);
            if (launched.IsFailure)
                return Result<OAuthToken>.Failure(launched.Errors);

            var callback = await listener.Value.WaitForCallbackAsync(cancellationToken).ConfigureAwait(false);
            if (callback.IsFailure)
                return Result<OAuthToken>.Failure(callback.Errors);

            if (callback.Value.TryGetValue("error", out var error))
            {
                _logger?.LogWarning("Authorization was refused by the authorization server: {Error}", error);
                return Result<OAuthToken>.Failure(AuthorizationRefused(error, callback.Value.GetValueOrDefault("error_description")));
            }

            if (!pkce.MatchesState(callback.Value.GetValueOrDefault("state")))
                return Result<OAuthToken>.Failure(StateMismatch);

            if (!callback.Value.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                return Result<OAuthToken>.Failure(CodeMissing);

            var grant = await _endpoint.ExchangeCodeAsync(code, pkce.Verifier, listener.Value.RedirectUri, cancellationToken).ConfigureAwait(false);
            if (grant.IsFailure)
                return Result<OAuthToken>.Failure(grant.Errors);

            if (string.IsNullOrEmpty(grant.Value.RefreshToken))
                return Result<OAuthToken>.Failure(NoRefreshToken);

            return Result<OAuthToken>.Success(OAuthToken.From(
                grant.Value.AccessToken,
                grant.Value.RefreshToken,
                grant.Value.TokenType,
                grant.Value.ExpiresAt,
                _options));
        }
    }

    /// <summary>Builds the authorization URL with every query value escaped.</summary>
    internal Uri BuildAuthorizeUri(Uri redirectUri, PkceChallenge pkce)
    {
        var query = new StringBuilder();
        Append(query, "response_type", "code");
        Append(query, "client_id", _options.ClientId);
        Append(query, "redirect_uri", redirectUri.AbsoluteUri);
        if (_options.Scopes.Count > 0)
            Append(query, "scope", string.Join(' ', _options.Scopes));
        Append(query, "state", pkce.State);
        Append(query, "code_challenge", pkce.Challenge);
        Append(query, "code_challenge_method", "S256");

        var builder = new UriBuilder(_options.AuthorizationEndpoint);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? query.ToString()
            : builder.Query.TrimStart('?') + "&" + query;
        return builder.Uri;
    }

    /// <summary>The failure returned when the authorization server answered the callback with an error.</summary>
    /// <param name="error">The OAuth error code, such as <c>access_denied</c>.</param>
    /// <param name="description">The server's own description, when it sent one.</param>
    public static Error AuthorizationRefused(string error, string? description) => Error.FromMessages(
        "Sign-in was not completed.",
        $"oauth: the authorization server answered '{error}'{(string.IsNullOrEmpty(description) ? string.Empty : ": " + description)}",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when the callback's state is missing or does not match the request's.</summary>
    public static Error StateMismatch { get; } = Error.FromMessages(
        "Sign-in was not completed.",
        "oauth: the callback's state did not match the request; the response was discarded",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when the callback carries neither a code nor an error.</summary>
    public static Error CodeMissing { get; } = Error.FromMessages(
        "Sign-in was not completed.",
        "oauth: the callback carried no authorization code",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the grant carries no refresh token and therefore cannot be stored.</summary>
    public static Error NoRefreshToken { get; } = Error.FromMessages(
        "Sign-in did not grant offline access.",
        "oauth: the authorization server issued no refresh token; the grant was refused",
        ErrorCategory.Validation);

    private static void Append(StringBuilder query, string name, string value)
    {
        if (query.Length > 0)
            query.Append('&');

        query.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
    }
}
