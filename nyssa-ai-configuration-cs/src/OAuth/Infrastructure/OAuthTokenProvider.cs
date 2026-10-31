using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

using NyssaAI.Configuration.Client;

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

    private const int PersistAttempts = 3;

    private readonly IConfigurationCipher _cipher;
    private readonly IConfigurationDocumentFactory _documents;
    private readonly HttpClient _client;
    private readonly IBrowserLauncher _browser;
    private readonly TimeSpan _refreshSkew;
    private readonly TimeProvider _clock;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, HeldGrant> _held = new(StringComparer.Ordinal);

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
        _cipher = cipher;
        _documents = documents;
        _client = client;
        _browser = browser;
        _refreshSkew = refreshSkew ?? TimeSpan.FromMinutes(5);
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        Name = name;
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
            var persisted = await PersistHeldAsync(@namespace, key, held, cancellationToken).ConfigureAwait(false);
            if (persisted.IsSuccess)
                return Result<string>.Success(held.Token.AccessToken);

            if (!persisted.Errors.Any(error => error.Category == ErrorCategory.Conflict))
                _held[address] = held;
            return Result<string>.Failure(persisted.Errors);
        }

        var token = Open(entry, @namespace, key);
        if (token.IsFailure)
            return Result<string>.Failure(token.Errors);

        if (IsFresh(token.Value))
            return Result<string>.Success(token.Value.AccessToken);

        return await RefreshUnderLockAsync(@namespace, key, cancellationToken).ConfigureAwait(false);
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

        return Seal(authorized.Value, @namespace, key);
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
        var stored = Open(entry, @namespace, key);
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
        var token = Parse(value);
        if (token.IsFailure)
            return ValueTask.FromResult(Result<ConfigurationValue>.Failure(token.Errors));

        if (string.IsNullOrWhiteSpace(token.Value.RefreshToken) || string.IsNullOrWhiteSpace(token.Value.AccessToken))
            return ValueTask.FromResult(Result<ConfigurationValue>.Failure(GrantIncomplete));

        return ValueTask.FromResult(Seal(token.Value, @namespace, key));
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

    private bool IsFresh(OAuthToken token) => token.ExpiresAt - _clock.GetUtcNow() > _refreshSkew;

    private async ValueTask<Result<string>> RefreshUnderLockAsync(string @namespace, string key, CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);

        Result<string>? outcome = null;
        HeldGrant? exchanged = null;

        var modified = await document.Value.ModifyAsync(
            async entries =>
            {
                // Re-read inside the lock: another reader may have refreshed while this one waited.
                if (!entries.TryGetValue(key, out var current) || !string.Equals(current.Provider, Name, StringComparison.Ordinal))
                {
                    outcome = Result<string>.Failure(EntryChanged(@namespace, key));
                    return Result.Failure(outcome.Errors);
                }

                if (current.ReauthRequired)
                {
                    outcome = Result<string>.Failure(ReauthorizationRequired(@namespace, key));
                    return Result.Failure(outcome.Errors);
                }

                var stored = Open(current, @namespace, key);
                if (stored.IsFailure)
                {
                    outcome = Result<string>.Failure(stored.Errors);
                    return Result.Failure(stored.Errors);
                }

                if (IsFresh(stored.Value))
                {
                    // The winner already stored a fresh grant; nothing to write.
                    outcome = Result<string>.Success(stored.Value.AccessToken);
                    return Result.Failure(NothingToWrite);
                }

                // The server is whatever this credential recorded when it was acquired.
                var endpoint = OAuthTokenEndpoint.Create(stored.Value.Options, _client, _clock, logger: _logger);
                if (endpoint.IsFailure)
                {
                    outcome = Result<string>.Failure(endpoint.Errors);
                    return Result.Failure(endpoint.Errors);
                }

                var refreshed = await endpoint.Value.RefreshAsync(stored.Value.RefreshToken, cancellationToken).ConfigureAwait(false);
                if (refreshed.IsFailure)
                {
                    if (refreshed.Errors.Any(error => error.Category == ErrorCategory.Forbidden))
                    {
                        // The grant is dead. Record that so every later read fails fast instead of
                        // presenting the same refresh token again.
                        _logger?.LogWarning("OAuth grant at {Namespace}/{Key} was rejected; re-authorization is required", @namespace, key);
                        entries[key] = current with { ReauthRequired = true };
                        outcome = Result<string>.Failure([ReauthorizationRequired(@namespace, key), .. refreshed.Errors]);
                        return Result.Success();
                    }

                    _logger?.LogWarning("OAuth refresh at {Namespace}/{Key} failed ({Category})", @namespace, key, refreshed.Errors[0].Category);
                    outcome = Result<string>.Failure(refreshed.Errors);
                    return Result.Failure(refreshed.Errors);
                }

                var rotated = stored.Value with
                {
                    AccessToken = refreshed.Value.AccessToken,
                    TokenType = refreshed.Value.TokenType,
                    ExpiresAt = refreshed.Value.ExpiresAt,
                    RefreshToken = string.IsNullOrEmpty(refreshed.Value.RefreshToken)
                        ? stored.Value.RefreshToken
                        : refreshed.Value.RefreshToken,
                };

                var sealedEntry = Seal(rotated, @namespace, key);
                if (sealedEntry.IsFailure)
                {
                    outcome = Result<string>.Failure(sealedEntry.Errors);
                    return Result.Failure(sealedEntry.Errors);
                }

                exchanged = new HeldGrant { Token = rotated, OriginalEntry = current };
                entries[key] = sealedEntry.Value;
                outcome = Result<string>.Success(rotated.AccessToken);
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);

        if (modified.IsSuccess || modified.Errors.Contains(NothingToWrite))
            return outcome!;

        if (exchanged is null)
            return outcome ?? Result<string>.Failure(modified.Errors);

        // The server rotated the grant and the publish failed. The exchanged token is the only
        // live one now, so it is retried here and held in memory if that fails too.
        var persisted = await PersistHeldAsync(@namespace, key, exchanged, cancellationToken).ConfigureAwait(false);
        if (persisted.IsSuccess)
            return Result<string>.Success(exchanged.Token.AccessToken);

        if (!persisted.Errors.Any(error => error.Category == ErrorCategory.Conflict))
        {
            _held[$"{@namespace}/{key}"] = exchanged;
            _logger?.LogWarning("OAuth grant at {Namespace}/{Key} was refreshed but could not be persisted; holding it for the next read", @namespace, key);
        }
        return Result<string>.Failure(persisted.Errors);
    }

    // Writes a token that has already been exchanged, without asking the server again. The
    // entry must still match the original snapshot or already contain the complete held grant,
    // so a concurrent refresh, Set, or Remove is never clobbered or resurrected.
    private async ValueTask<Result> PersistHeldAsync(string @namespace, string key, HeldGrant held, CancellationToken cancellationToken)
    {
        Result last = Result.Success();
        for (var attempt = 1; attempt <= PersistAttempts; attempt++)
        {
            var document = _documents.Create(@namespace);
            if (document.IsFailure)
                return Result.Failure(document.Errors);

            last = await document.Value.ModifyAsync(
                entries =>
                {
                    if (!entries.TryGetValue(key, out var current) || !string.Equals(current.Provider, Name, StringComparison.Ordinal))
                        return ValueTask.FromResult(Result.Failure(EntryChanged(@namespace, key)));

                    var stored = Open(current, @namespace, key);
                    if (stored.IsFailure)
                        return ValueTask.FromResult(Result.Failure(stored.Errors));

                    // A server may keep the same refresh token while issuing a new access token.
                    // Only the complete persisted grant proves the failed publish already landed.
                    if (!current.ReauthRequired && SameGrant(stored.Value, held.Token))
                        return ValueTask.FromResult(Result.Failure(NothingToWrite));

                    if (current != held.OriginalEntry)
                        return ValueTask.FromResult(Result.Failure(EntryChanged(@namespace, key)));

                    var sealedEntry = Seal(held.Token, @namespace, key);
                    if (sealedEntry.IsFailure)
                        return ValueTask.FromResult(Result.Failure(sealedEntry.Errors));

                    entries[key] = sealedEntry.Value;
                    return ValueTask.FromResult(Result.Success());
                },
                cancellationToken).ConfigureAwait(false);

            if (last.IsSuccess || last.Errors.Contains(NothingToWrite))
                return Result.Success();

            // A changed entry or a canceled caller will not improve on retry.
            if (last.Errors.Any(error => error.Category is ErrorCategory.Conflict or ErrorCategory.Canceled or ErrorCategory.Validation))
                return last;
        }

        return last;
    }

    private static bool SameGrant(OAuthToken stored, OAuthToken held) =>
        stored.AccessToken == held.AccessToken
        && stored.RefreshToken == held.RefreshToken
        && stored.TokenType == held.TokenType
        && stored.ExpiresAt == held.ExpiresAt
        && stored.AuthorizationEndpoint == held.AuthorizationEndpoint
        && stored.TokenEndpoint == held.TokenEndpoint
        && stored.ClientId == held.ClientId
        && stored.Scopes.SequenceEqual(held.Scopes, StringComparer.Ordinal)
        && stored.RedirectPort == held.RedirectPort;

    private Result<OAuthToken> Open(ConfigurationValue entry, string @namespace, string key)
    {
        var opened = _cipher.Open(entry.Value, @namespace, key);
        return opened.IsFailure ? Result<OAuthToken>.Failure(opened.Errors) : Parse(opened.Value);
    }

    private Result<ConfigurationValue> Seal(OAuthToken token, string @namespace, string key)
    {
        var sealedValue = _cipher.Seal(JsonSerializer.Serialize(token, OAuthGrantJsonContext.Default.OAuthToken), @namespace, key);
        return sealedValue.IsFailure
            ? Result<ConfigurationValue>.Failure(sealedValue.Errors)
            : Result<ConfigurationValue>.Success(new ConfigurationValue
            {
                Value = sealedValue.Value,
                Encrypted = true,
                Provider = Name,
            });
    }

    // This is the adapter boundary: System.Text.Json throws on a malformed payload.
    private static Result<OAuthToken> Parse(string payload)
    {
        OAuthToken? token;
        try
        {
            token = JsonSerializer.Deserialize(payload, OAuthGrantJsonContext.Default.OAuthToken);
        }
        catch (JsonException exception)
        {
            return Result<OAuthToken>.Failure(TokenPayloadInvalid(exception));
        }

        return token is null
            ? Result<OAuthToken>.Failure(TokenPayloadMissing)
            : Result<OAuthToken>.Success(token);
    }

    // A change callback returns this to say "the document is already right; do not publish". It
    // never leaves this type: every caller replaces it with the outcome it stands for.
    private static readonly Error NothingToWrite = Error.FromMessages(
        "Nothing to write.",
        "oauth: the stored grant is already current",
        ErrorCategory.Conflict);

    private sealed record HeldGrant
    {
        public required OAuthToken Token { get; init; }

        public required ConfigurationValue OriginalEntry { get; init; }
    }
}
