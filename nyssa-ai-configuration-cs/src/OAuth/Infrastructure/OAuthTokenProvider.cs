using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// OAuthTokenProvider stores OAuth credentials sealed in the configuration document and hands out
/// their access tokens, refreshing a credential when it is inside the refresh skew of expiring.
/// </summary>
/// <remarks>
/// <para>
/// OAuth is a credential type here, not an identity of this library. Every stored credential
/// describes the server it came from, so one provider serves every service the app talks to and
/// every provider package that signs its requests with a bearer token.
/// </para>
/// <para>
/// A token valid for longer than the skew takes the unlocked fast path. Otherwise the provider
/// takes the namespace document's lock, re-reads the entry inside it, and refreshes only if it is
/// still stale -- so N readers past the skew boundary, in one process or several, produce one
/// token-endpoint call, and the rest return the token the winner stored. The rotated refresh token
/// is written in the same locked publish that hands the access token back.
/// </para>
/// <para>
/// A grant the server rejects marks the entry <see cref="ConfigurationValue.ReauthRequired"/> and is
/// reported as <see cref="ErrorCategory.Forbidden"/>; every later read fails fast until a new
/// grant is written. When the exchange succeeds and the publish fails, the publish is retried with
/// the token already in hand and, if it keeps failing, the token is held in memory so the next
/// read retries the publish rather than asking the server to rotate again.
/// </para>
/// <para>
/// Reads never open a browser. Only <see cref="AuthorizeAsync"/> and <see cref="ReauthorizeAsync"/>
/// do, and only because the app called them from a context it chose.
/// </para>
/// </remarks>
internal sealed class OAuthTokenProvider : IConfigurationProvider, IReauthorizableProvider
{
    public bool IsLocalOnly => true;

    private readonly HttpClient _client;
    private readonly IBrowserLauncher _browser;
    private readonly TimeProvider _clock;
    private readonly ILogger? _logger;
    private readonly OAuthGrantStore _grantStore;
    private readonly ConcurrentDictionary<string, OAuthHeldGrant> _held = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    /// <param name="cipher">The cipher that seals and opens stored credentials.</param>
    /// <param name="documents">The factory opening the namespace document a rotated grant is written back to.</param>
    /// <param name="client">The HTTP client every token-endpoint exchange goes through; borrowed.</param>
    /// <param name="browser">What opens the authorize URI during an interactive flow.</param>
    /// <param name="clock">The clock used to compare against expiry; the system clock when null.</param>
    /// <param name="logger">The logger refresh and authorization outcomes are reported to, if any.</param>
    /// <param name="refreshSkew">How far ahead of expiry to refresh; five minutes when null.</param>
    /// <param name="name">The name entries use to select this provider.</param>
    public OAuthTokenProvider(
        IConfigurationCipher cipher,
        IConfigurationDocumentFactory documents,
        HttpClient client,
        IBrowserLauncher browser,
        TimeProvider? clock = null,
        ILogger? logger = null,
        TimeSpan? refreshSkew = null,
        string name = "oauth")
    {
        _client = client;
        _browser = browser;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        Name = name;
        _grantStore = new OAuthGrantStore(
            cipher,
            documents,
            client,
            _clock,
            refreshSkew ?? TimeSpan.FromMinutes(5),
            name,
            logger);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public async ValueTask<Result<string>> ReadAsync(ConfigurationValue entry, string @namespace, string key, CancellationToken cancellationToken = default)
    {
        if (entry.ReauthRequired)
            return Result<string>.Failure(ReauthorizationRequired(@namespace, key));

        var address = $"{@namespace}/{key}";
        if (_held.TryRemove(address, out var held))
        {
            var persisted = await _grantStore.PersistHeldAsync(@namespace, key, held, cancellationToken).ConfigureAwait(false);
            if (persisted.IsSuccess)
                return Result<string>.Success(held.Token.AccessToken);

            if (!persisted.Errors.Any(error => error.Category == ErrorCategory.Conflict))
                _held[address] = held;
            return Result<string>.Failure(persisted.Errors);
        }

        var token = _grantStore.Open(entry, @namespace, key);
        if (token.IsFailure)
            return Result<string>.Failure(token.Errors);

        if (_grantStore.IsFresh(token.Value))
            return Result<string>.Success(token.Value.AccessToken);

        return await _grantStore.RefreshUnderLockAsync(@namespace, key, _held, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationValue>> AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken = default)
    {
        _held.TryRemove($"{@namespace}/{key}", out _);

        var flow = OAuthAuthorizationClientLoopback.Create(options, _client, _browser, _clock, _logger);
        if (flow.IsFailure)
            return Result<ConfigurationValue>.Failure(flow.Errors);

        var authorized = await flow.Value.AuthorizeAsync(cancellationToken).ConfigureAwait(false);
        if (authorized.IsFailure)
        {
            _logger?.LogWarning(
                "Authorization for {Namespace}/{Key} did not complete ({Category})",
                @namespace, key, authorized.Errors[0].Category);
            return Result<ConfigurationValue>.Failure(authorized.Errors);
        }

        return _grantStore.Seal(authorized.Value, @namespace, key);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The refresh token is gone, so there is nothing to exchange: only the user can produce a new
    /// grant. The server is read from the entry, so the app supplies nothing and cannot supply the
    /// wrong one. Any grant held in memory for this address is dropped first, because it is the one
    /// the server already rejected.
    /// </remarks>
    public ValueTask<Result<ConfigurationValue>> ReauthorizeAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        var stored = _grantStore.Open(entry, @namespace, key);
        return stored.IsFailure
            ? ValueTask.FromResult(Result<ConfigurationValue>.Failure(stored.Errors))
            : AuthorizeAsync(@namespace, key, stored.Value.Options, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A credential arrives here only through a move from another provider, as the sealed JSON of a
    /// self-describing grant. Authorization is the way to create one.
    /// </remarks>
    public ValueTask<Result<ConfigurationValue>> WriteAsync(string value, string @namespace, string key, CancellationToken cancellationToken = default)
    {
        var token = OAuthGrantStore.Parse(value);
        if (token.IsFailure)
            return ValueTask.FromResult(Result<ConfigurationValue>.Failure(token.Errors));

        if (string.IsNullOrWhiteSpace(token.Value.RefreshToken) || string.IsNullOrWhiteSpace(token.Value.AccessToken))
            return ValueTask.FromResult(Result<ConfigurationValue>.Failure(GrantIncomplete));

        return ValueTask.FromResult(_grantStore.Seal(token.Value, @namespace, key));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The sealed grant is the entry's own value, so the store removing or overwriting the entry is the
    /// withdrawal. What this does own is the grant held in memory for that address after a publish
    /// failure; dropping it here stops a later read from re-publishing a grant the caller has moved
    /// somewhere else or removed. This does not revoke credentials at the authorization server.
    /// </remarks>
    public ValueTask<Result<ConfigurationRemoval>> EraseAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken = default)
    {
        _held.TryRemove($"{@namespace}/{key}", out _);
        return ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = false }));
    }

    /// <summary>The failure returned when the stored grant needs the user to authorize again.</summary>
    public static Error ReauthorizationRequired(string @namespace, string key) => Error.FromMessages(
        "You need to sign in again.",
        $"oauth: re-authorization required for '{@namespace}/{key}'; the stored refresh token was rejected",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when the sealed grant is not well-formed JSON.</summary>
    public static Error TokenPayloadInvalid(Exception exception) => Error.FromMessages(
        "The stored sign-in could not be read.",
        $"The OAuth token payload is not valid JSON: {exception.Message}",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the sealed grant deserializes to null.</summary>
    public static Error TokenPayloadMissing { get; } = Error.FromMessages(
        "The stored sign-in could not be read.",
        "The OAuth token payload deserialized to null.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a grant offered for storage carries no refresh or access token.</summary>
    public static Error GrantIncomplete { get; } = Error.FromMessages(
        "The sign-in cannot be stored.",
        "An OAuth grant must carry both an access token and a refresh token to be stored.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the entry changed under the lock before a grant could be written back.</summary>
    public static Error EntryChanged(string @namespace, string key) => Error.FromMessages(
        "The stored sign-in changed while it was being refreshed.",
        $"The OAuth entry at '{@namespace}/{key}' was removed or replaced during refresh.",
        ErrorCategory.Conflict);
}
