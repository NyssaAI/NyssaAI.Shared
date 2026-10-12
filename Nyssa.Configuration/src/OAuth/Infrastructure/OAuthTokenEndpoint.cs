using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.OAuth.Domain;

using NyssaAI.Configuration.Client;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// OAuthTokenEndpoint speaks to one token endpoint: it posts a refresh-token or
/// authorization-code grant and classifies what comes back. Both the refresh path and the
/// interactive authorization flow exchange through this one type.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="HttpClient"/> is borrowed, never owned: the provider that composes these holds
/// one client for its lifetime, obtained from <c>IHttpClientFactory</c> when the host registered
/// one. That is how a test points every exchange at a fake handler and never reaches the network.
/// </para>
/// <para>
/// This is the repository's HTTP adapter boundary. A rejected grant (<c>400</c>, <c>401</c>,
/// <c>403</c>) is <see cref="ErrorCategory.Forbidden"/> and is never retried; a transport
/// failure, a timeout, and a <c>5xx</c> are <see cref="ErrorCategory.Unavailable"/>; the caller's
/// own cancellation is <see cref="ErrorCategory.Canceled"/>. No failure message carries a token
/// or a response body.
/// </para>
/// </remarks>
internal sealed class OAuthTokenEndpoint
{
    /// <summary>The default bound on one exchange.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(1);

    private readonly HttpClient _client;
    private readonly OAuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;

    private OAuthTokenEndpoint(HttpClient client, OAuthOptions options, TimeProvider clock, TimeSpan timeout, ILogger? logger)
    {
        _client = client;
        _options = options;
        _clock = clock;
        _timeout = timeout;
        _logger = logger;
    }

    /// <summary>Creates an endpoint client, failing when the options or timeout are not valid.</summary>
    /// <param name="options">The authorization server and client.</param>
    /// <param name="client">The HTTP client to send through; borrowed, never disposed here.</param>
    /// <param name="clock">The clock expiry is computed against; the system clock when null.</param>
    /// <param name="timeout">The bound on one exchange; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="logger">The logger exchange outcomes are reported to, if any.</param>
    public static Result<OAuthTokenEndpoint> Create(
        OAuthOptions options,
        HttpClient client,
        TimeProvider? clock = null,
        TimeSpan? timeout = null,
        ILogger? logger = null)
    {
        var valid = options.Validate();
        if (valid.IsFailure)
            return Result<OAuthTokenEndpoint>.Failure(valid.Errors);

        if (timeout is { } supplied && supplied <= TimeSpan.Zero)
            return Result<OAuthTokenEndpoint>.Failure(TimeoutInvalid(supplied));

        return Result<OAuthTokenEndpoint>.Success(
            new OAuthTokenEndpoint(client, options, clock ?? TimeProvider.System, timeout ?? DefaultTimeout, logger));
    }

    /// <summary>Exchanges a refresh token for a new grant.</summary>
    public ValueTask<Result<OAuthGrant>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        PostAsync(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", _options.ClientId),
            ],
            cancellationToken);

    /// <summary>Exchanges an authorization code and its PKCE verifier for a grant.</summary>
    public ValueTask<Result<OAuthGrant>> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken = default) =>
        PostAsync(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", redirectUri.AbsoluteUri),
                new("client_id", _options.ClientId),
                new("code_verifier", codeVerifier),
            ],
            cancellationToken);

    /// <summary>The failure returned when the server rejected the grant.</summary>
    public static Error GrantRejected(HttpStatusCode status, string? error) => Error.FromMessages(
        "You need to sign in again.",
        $"oauth: the token endpoint rejected the grant ({(int)status}{(error is null ? string.Empty : ", " + error)})",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when the server could not be reached or answered with a server error.</summary>
    public static Error EndpointUnavailable(string detail) => Error.FromMessages(
        "The sign-in service could not be reached. Try again.",
        $"oauth: {detail}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the server answered with a status this client does not classify.</summary>
    public static Error UnexpectedStatus(HttpStatusCode status) => Error.FromMessages(
        "The sign-in service answered unexpectedly.",
        $"oauth: the token endpoint answered {(int)status}",
        ErrorCategory.Unexpected);

    /// <summary>The failure returned when the response body is not a JSON token response.</summary>
    public static Error ResponseNotJson { get; } = Error.FromMessages(
        "The sign-in service answered unexpectedly.",
        "oauth: the token endpoint's response was not a JSON token response",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a success response carries no access token.</summary>
    public static Error ResponseMissingAccessToken { get; } = Error.FromMessages(
        "The sign-in service answered unexpectedly.",
        "oauth: the token endpoint's response carried no access_token",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the exchange timeout is zero or negative.</summary>
    public static Error TimeoutInvalid(TimeSpan timeout) => Error.FromMessages(
        "The sign-in configuration is not valid.",
        $"oauth: the exchange timeout must be positive; {timeout} was supplied",
        ErrorCategory.Validation);

    private async ValueTask<Result<OAuthGrant>> PostAsync(List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        HttpStatusCode status;
        string body;

        // The bound is applied per exchange rather than on the shared client, which other
        // callers may be using with different expectations.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_timeout);

        // This is the adapter boundary: HttpClient throws for transport failures, the bound, and
        // the caller's cancellation, and each maps to a different category.
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _client.PostAsync(_options.TokenEndpoint, content, bounded.Token).ConfigureAwait(false);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled) when (cancellationToken.IsCancellationRequested)
        {
            return Result<OAuthGrant>.Failure(Error.Canceled(canceled));
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Token exchange with {Endpoint} timed out after {Timeout}", _options.TokenEndpoint, _timeout);
            return Result<OAuthGrant>.Failure(EndpointUnavailable($"the exchange exceeded its {_timeout.TotalSeconds:0.###}s timeout"));
        }
        catch (HttpRequestException exception)
        {
            _logger?.LogWarning("Token exchange with {Endpoint} failed: {Reason}", _options.TokenEndpoint, exception.Message);
            return Result<OAuthGrant>.Failure(EndpointUnavailable($"the request failed: {exception.Message}"));
        }

        var parsed = Parse(body);

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result<OAuthGrant>.Failure(GrantRejected(status, parsed?.Error));

        if ((int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            return Result<OAuthGrant>.Failure(EndpointUnavailable($"the token endpoint answered {(int)status}"));

        if (status is not HttpStatusCode.OK)
            return Result<OAuthGrant>.Failure(UnexpectedStatus(status));

        if (parsed is null)
            return Result<OAuthGrant>.Failure(ResponseNotJson);

        if (string.IsNullOrWhiteSpace(parsed.AccessToken))
            return Result<OAuthGrant>.Failure(ResponseMissingAccessToken);

        var lifetime = parsed.ExpiresIn is > 0 ? TimeSpan.FromSeconds(parsed.ExpiresIn.Value) : DefaultExpiry;
        return Result<OAuthGrant>.Success(new OAuthGrant
        {
            AccessToken = parsed.AccessToken,
            RefreshToken = string.IsNullOrEmpty(parsed.RefreshToken) ? null : parsed.RefreshToken,
            TokenType = string.IsNullOrWhiteSpace(parsed.TokenType) ? "Bearer" : parsed.TokenType,
            ExpiresAt = _clock.GetUtcNow() + lifetime,
        });
    }

    // System.Text.Json throws on a body that is not JSON; a null here is that case, and the
    // caller decides what it means for the status it saw. The body is never copied into an error.
    private static OAuthTokenResponse? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize(body, OAuthWireJsonContext.Default.OAuthTokenResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
