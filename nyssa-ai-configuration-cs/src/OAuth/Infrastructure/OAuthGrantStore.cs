using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// Manages sealing, opening, locked refresh, and retry persistence of OAuth grants in namespace documents.
/// </summary>
internal sealed class OAuthGrantStore
{
    private const int PersistAttempts = 3;

    // A change callback returns this to say "the document is already right; do not publish". It
    // never leaves this type: every caller replaces it with the outcome it stands for.
    private static readonly Error NothingToWrite = Error.FromMessages(
        "Nothing to write.",
        "oauth: the stored grant is already current",
        ErrorCategory.Conflict);

    private readonly IConfigurationCipher _cipher;
    private readonly IConfigurationDocumentFactory _documents;
    private readonly HttpClient _client;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _refreshSkew;
    private readonly string _providerName;
    private readonly ILogger? _logger;

    public OAuthGrantStore(
        IConfigurationCipher cipher,
        IConfigurationDocumentFactory documents,
        HttpClient client,
        TimeProvider clock,
        TimeSpan refreshSkew,
        string providerName,
        ILogger? logger)
    {
        _cipher = cipher;
        _documents = documents;
        _client = client;
        _clock = clock;
        _refreshSkew = refreshSkew;
        _providerName = providerName;
        _logger = logger;
    }

    public bool IsFresh(OAuthToken token) => token.ExpiresAt - _clock.GetUtcNow() > _refreshSkew;

    public Result<OAuthToken> Open(ConfigurationValue entry, string @namespace, string key)
    {
        var opened = _cipher.Open(entry.Value, @namespace, key);
        return opened.IsFailure ? Result<OAuthToken>.Failure(opened.Errors) : Parse(opened.Value);
    }

    public Result<ConfigurationValue> Seal(OAuthToken token, string @namespace, string key)
    {
        var sealedValue = _cipher.Seal(JsonSerializer.Serialize(token, OAuthGrantJsonContext.Default.OAuthToken), @namespace, key);
        return sealedValue.IsFailure
            ? Result<ConfigurationValue>.Failure(sealedValue.Errors)
            : Result<ConfigurationValue>.Success(new ConfigurationValue
            {
                Value = sealedValue.Value,
                Encrypted = true,
                Provider = _providerName,
            });
    }

    // This is the adapter boundary: System.Text.Json throws on a malformed payload.
    public static Result<OAuthToken> Parse(string payload)
    {
        OAuthToken? token;
        try
        {
            token = JsonSerializer.Deserialize(payload, OAuthGrantJsonContext.Default.OAuthToken);
        }
        catch (JsonException exception)
        {
            return Result<OAuthToken>.Failure(OAuthTokenProvider.TokenPayloadInvalid(exception));
        }

        return token is null
            ? Result<OAuthToken>.Failure(OAuthTokenProvider.TokenPayloadMissing)
            : Result<OAuthToken>.Success(token);
    }

    public async ValueTask<Result<string>> RefreshUnderLockAsync(
        string @namespace,
        string key,
        ConcurrentDictionary<string, OAuthHeldGrant> heldGrants,
        CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);

        Result<string>? outcome = null;
        OAuthHeldGrant? exchanged = null;

        var modified = await document.Value.ModifyAsync(
            async entries =>
            {
                // Re-read inside the lock: another reader may have refreshed while this one waited.
                if (!entries.TryGetValue(key, out var current) || !string.Equals(current.Provider, _providerName, StringComparison.Ordinal))
                {
                    outcome = Result<string>.Failure(OAuthTokenProvider.EntryChanged(@namespace, key));
                    return Result.Failure(outcome.Errors);
                }

                if (current.ReauthRequired)
                {
                    outcome = Result<string>.Failure(OAuthTokenProvider.ReauthorizationRequired(@namespace, key));
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
                        outcome = Result<string>.Failure([OAuthTokenProvider.ReauthorizationRequired(@namespace, key), .. refreshed.Errors]);
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

                exchanged = new OAuthHeldGrant { Token = rotated, OriginalEntry = current };
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
            heldGrants[$"{@namespace}/{key}"] = exchanged;
            _logger?.LogWarning("OAuth grant at {Namespace}/{Key} was refreshed but could not be persisted; holding it for the next read", @namespace, key);
        }
        return Result<string>.Failure(persisted.Errors);
    }

    // Writes a token that has already been exchanged, without asking the server again. The
    // entry must still match the original snapshot or already contain the complete held grant,
    // so a concurrent refresh, Set, or Remove is never clobbered or resurrected.
    public async ValueTask<Result> PersistHeldAsync(string @namespace, string key, OAuthHeldGrant held, CancellationToken cancellationToken)
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
                    if (!entries.TryGetValue(key, out var current) || !string.Equals(current.Provider, _providerName, StringComparison.Ordinal))
                        return ValueTask.FromResult(Result.Failure(OAuthTokenProvider.EntryChanged(@namespace, key)));

                    var stored = Open(current, @namespace, key);
                    if (stored.IsFailure)
                        return ValueTask.FromResult(Result.Failure(stored.Errors));

                    // A server may keep the same refresh token while issuing a new access token.
                    // Only the complete persisted grant proves the failed publish already landed.
                    if (!current.ReauthRequired && SameGrant(stored.Value, held.Token))
                        return ValueTask.FromResult(Result.Failure(NothingToWrite));

                    if (current != held.OriginalEntry)
                        return ValueTask.FromResult(Result.Failure(OAuthTokenProvider.EntryChanged(@namespace, key)));

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
}
