using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// ConfigurationStore resolves configuration values, reading literals straight from the document
/// and delegating provider-backed entries to the registered <see cref="IConfigurationProvider"/>.
/// </summary>
/// <remarks>
/// Construct a store with providers through <c>Create</c>, which refuses a blank or duplicate
/// provider name as a failure. The constructor takes no providers and cannot fail.
/// The store accepts an <see cref="ILogger"/> and chooses no destination; provider failures are
/// reported there once, by namespace and key, never by value.
/// </remarks>
internal sealed partial class ConfigurationStore : IConfigurationValues
{
    private const string OpenGenericJustification =
        "TValue is caller-supplied and cannot be enumerated into a source-generated context at this project's compile time; "
        + "callers publishing with Native AOT use the overload that takes a JsonTypeInfo<TValue>.";

    private readonly IConfigurationDocumentFactory _documents;
    private readonly IReadOnlyDictionary<string, IConfigurationProvider> _providers;
    private readonly IReadOnlyDictionary<string, ProviderCredential> _credentials;
    private readonly ILogger? _logger;

    /// <summary>Creates a store over one document factory with no providers; every entry is a literal.</summary>
    /// <param name="documents">The factory that opens a namespace's document.</param>
    /// <param name="logger">The logger failures are reported to, if any.</param>
    public ConfigurationStore(IConfigurationDocumentFactory documents, ILogger? logger = null)
        : this(
            documents,
            new Dictionary<string, IConfigurationProvider>(StringComparer.Ordinal),
            new Dictionary<string, ProviderCredential>(StringComparer.Ordinal),
            logger)
    {
    }

    private ConfigurationStore(
        IConfigurationDocumentFactory documents,
        IReadOnlyDictionary<string, IConfigurationProvider> providers,
        IReadOnlyDictionary<string, ProviderCredential> credentials,
        ILogger? logger)
    {
        _documents = documents;
        _providers = providers;
        _credentials = credentials;
        _logger = logger;
    }

    /// <summary>
    /// Creates a store over one document factory and the providers it may resolve through,
    /// failing when a provider name is blank or registered twice.
    /// </summary>
    /// <param name="documents">The factory that opens a namespace's document.</param>
    /// <param name="providers">The providers available to resolve non-literal entries.</param>
    /// <param name="logger">The logger failures are reported to, if any.</param>
    public static Result<ConfigurationStore> Create(
        IConfigurationDocumentFactory documents,
        IEnumerable<IConfigurationProvider> providers,
        ILogger? logger = null) =>
        Create(documents, providers, [], logger);

    /// <summary>
    /// Creates a store over one document factory, the providers it may resolve through, and the
    /// credentials those providers authenticate with, failing when a provider name is blank or
    /// registered twice or a credential names a provider that was not supplied.
    /// </summary>
    /// <param name="documents">The factory that opens a namespace's document.</param>
    /// <param name="providers">The providers available to resolve non-literal entries.</param>
    /// <param name="credentials">Which entry each authenticating provider signs in with.</param>
    /// <param name="logger">The logger failures are reported to, if any.</param>
    public static Result<ConfigurationStore> Create(
        IConfigurationDocumentFactory documents,
        IEnumerable<IConfigurationProvider> providers,
        IEnumerable<ProviderCredential> credentials,
        ILogger? logger = null)
    {
        var registered = new Dictionary<string, IConfigurationProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
                return Result<ConfigurationStore>.Failure(ProviderNameBlank);

            if (!registered.TryAdd(provider.Name, provider))
                return Result<ConfigurationStore>.Failure(ProviderNameDuplicate(provider.Name));
        }

        var declared = new Dictionary<string, ProviderCredential>(StringComparer.Ordinal);
        foreach (var credential in credentials)
        {
            if (!registered.ContainsKey(credential.Provider))
                return Result<ConfigurationStore>.Failure(ProviderNotRegistered(credential.Provider));

            declared[credential.Provider] = credential;
        }

        return Result<ConfigurationStore>.Success(new ConfigurationStore(documents, registered, declared, logger));
    }

    /// <summary>
    /// Resolves the credential a provider authenticates with, for that provider's token source.
    /// </summary>
    /// <param name="provider">The provider asking.</param>
    /// <param name="cancellationToken">Token used to cancel the read or refresh.</param>
    /// <returns>
    /// The access token; authorization required when the entry does not exist yet; circular
    /// credential when the entry is stored inside the very provider asking for it.
    /// </returns>
    public async ValueTask<Result<string>> ResolveCredentialAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (!_credentials.TryGetValue(provider, out var credential))
            return Result<string>.Failure(ProviderHasNoCredential(provider));

        var document = _documents.Create(credential.Namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);

        var entries = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (entries.IsFailure)
            return Result<string>.Failure(entries.Errors);

        if (!entries.Value.TryGetValue(credential.Key, out var entry))
            return Result<string>.Failure(AuthorizationRequired(provider));

        // Checked on the envelope, before anything is opened: resolving the value would already
        // be the circular call.
        if (string.Equals(entry.Provider, provider, StringComparison.Ordinal))
            return Result<string>.Failure(CircularCredential(provider, credential.Address));

        return await GetAsync(credential.Namespace, credential.Key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Result> AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken = default)
    {
        var authorizer = GetAuthorizingProvider();
        if (authorizer.IsFailure)
            return Result.Failure(authorizer.Errors);

        var circular = CheckCircular(authorizer.Value.Name, @namespace, key);
        if (circular.IsFailure)
            return circular;

        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);

        // The flow runs inside the document's own exclusion, like every other entry change, so a
        // concurrent read cannot observe a half-written credential.
        return await document.Value.ModifyAsync(
            async entries =>
            {
                if (entries.TryGetValue(key, out var existing)
                    && !string.Equals(existing.Provider, authorizer.Value.Name, StringComparison.Ordinal))
                    return Result.Failure(AlreadyStoredElsewhere(@namespace, key, existing.Provider));

                var authorized = await ((IReauthorizableProvider)authorizer.Value)
                    .AuthorizeAsync(@namespace, key, options, cancellationToken).ConfigureAwait(false);
                if (authorized.IsFailure)
                    return Result.Failure(authorized.Errors);

                entries[key] = authorized.Value;
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<Result> AuthorizeProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (!_credentials.TryGetValue(provider, out var credential))
            return ValueTask.FromResult(Result.Failure(ProviderHasNoCredential(provider)));

        if (credential.Authorization is null)
            return ValueTask.FromResult(Result.Failure(ProviderNotAuthorizing(provider)));

        return AuthorizeAsync(credential.Namespace, credential.Key, credential.Authorization, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<string>> GetAsync(string @namespace, string key, CancellationToken cancellationToken = default)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);

        var entries = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (entries.IsFailure)
            return Result<string>.Failure(entries.Errors);

        if (!entries.Value.TryGetValue(key, out var entry))
            return Result<string>.Failure(EntryNotFound(@namespace, key));

        var validation = entry.Validate();
        if (validation.IsFailure)
            return Result<string>.Failure(validation.Errors);

        // The entry names the provider that wrote it, so a read never has to be told how the value
        // was stored. An entry naming nothing is a literal and resolves to itself.
        if (entry.IsLiteral)
            return Result<string>.Success(entry.Value);

        var provider = GetProvider(entry.Provider);
        if (provider.IsFailure)
            return Result<string>.Failure(provider.Errors);

        var resolved = await provider.Value.ReadAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            _logger?.LogWarning(
                "Provider '{Provider}' could not resolve {Namespace}/{Key} ({Category})",
                entry.Provider, @namespace, key, resolved.Errors[0].Category);
        }

        return resolved;
    }

    /// <inheritdoc/>
    public ValueTask<Result> SetAsync(
        string @namespace,
        string key,
        string value,
        string? provider = null,
        CancellationToken cancellationToken = default) =>
        SetValueAsync(@namespace, key, value, provider, false, cancellationToken);

    private async ValueTask<Result> SetValueAsync(
        string @namespace, string key, string value, string? provider,
        bool isJsonValue, CancellationToken cancellationToken)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);

        return await document.Value.ModifyAsync(
            async entries =>
            {
                var exists = entries.TryGetValue(key, out var existing);

                // An overwrite inherits where the value already lives. That is what stops a write
                // from quietly turning a sealed secret into a plaintext literal, which is exactly
                // what happens when the destination is re-decided on every call.
                var destination = provider ?? (exists ? existing!.Provider : null);
                if (destination is null)
                    return Result.Failure(DestinationRequired(@namespace, key));

                if (exists && !string.Equals(destination, existing!.Provider, StringComparison.Ordinal))
                    return Result.Failure(AlreadyStoredElsewhere(@namespace, key, existing.Provider));

                var circular = CheckCircular(destination, @namespace, key);
                if (circular.IsFailure)
                    return circular;

                if (destination.Length == 0)
                {
                    entries[key] = new ConfigurationValue { Value = value, IsJsonValue = isJsonValue };
                    return Result.Success();
                }

                var target = GetProvider(destination);
                if (target.IsFailure)
                    return Result.Failure(target.Errors);

                var written = await target.Value.WriteAsync(value, @namespace, key, cancellationToken).ConfigureAwait(false);
                if (written.IsFailure)
                {
                    _logger?.LogWarning(
                        "Provider '{Provider}' could not store {Namespace}/{Key} ({Category})",
                        destination, @namespace, key, written.Errors[0].Category);
                    return Result.Failure(written.Errors);
                }

                entries[key] = written.Value with { IsJsonValue = isJsonValue };
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Result> ReauthorizeAsync(string @namespace, string key, CancellationToken cancellationToken = default)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);

        // The flow runs inside the document's own exclusion, like every other entry change, so a
        // concurrent read cannot observe the stale entry after the new credential is obtained.
        return await document.Value.ModifyAsync(
            async entries =>
            {
                if (!entries.TryGetValue(key, out var entry))
                    return Result.Failure(EntryNotFound(@namespace, key));

                if (entry.IsLiteral)
                    return Result.Failure(EntryIsLiteral(@namespace, key));

                var provider = GetProvider(entry.Provider);
                if (provider.IsFailure)
                    return Result.Failure(provider.Errors);

                if (provider.Value is not IReauthorizableProvider reauthorizable)
                    return Result.Failure(ProviderNotReauthorizable(entry.Provider));

                var authorized = await reauthorizable.ReauthorizeAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
                if (authorized.IsFailure)
                {
                    _logger?.LogWarning(
                        "Provider '{Provider}' could not re-authorize {Namespace}/{Key} ({Category})",
                        entry.Provider, @namespace, key, authorized.Errors[0].Category);
                    return Result.Failure(authorized.Errors);
                }

                entries[key] = authorized.Value;
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<ConfigurationMove>> MoveAsync(
        string @namespace,
        string key,
        string toProvider,
        CancellationToken cancellationToken = default)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<ConfigurationMove>.Failure(document.Errors);

        // Captured inside the lock and read after it, so the outcome describes the entry that was
        // actually published rather than whatever the document holds by the time we return.
        ConfigurationValue? source = null;
        string from = string.Empty;

        var published = await document.Value.ModifyAsync(
            async entries =>
            {
                if (!entries.TryGetValue(key, out var existing))
                    return Result.Failure(EntryNotFound(@namespace, key));

                if (string.Equals(existing.Provider, toProvider, StringComparison.Ordinal))
                    return Result.Failure(AlreadyThere(@namespace, key, toProvider));

                var circular = CheckCircular(toProvider, @namespace, key);
                if (circular.IsFailure)
                    return circular;

                var plaintext = existing.IsLiteral
                    ? Result<string>.Success(existing.Value)
                    : await ReadThroughAsync(existing, @namespace, key, cancellationToken).ConfigureAwait(false);
                if (plaintext.IsFailure)
                    return Result.Failure(plaintext.Errors);

                if (toProvider.Length == 0)
                {
                    entries[key] = new ConfigurationValue { Value = plaintext.Value, IsJsonValue = existing.IsJsonValue };
                }
                else
                {
                    var target = GetProvider(toProvider);
                    if (target.IsFailure)
                        return Result.Failure(target.Errors);

                    var written = await target.Value.WriteAsync(plaintext.Value, @namespace, key, cancellationToken).ConfigureAwait(false);
                    if (written.IsFailure)
                        return Result.Failure(written.Errors);

                    entries[key] = written.Value with { IsJsonValue = existing.IsJsonValue };
                }

                source = existing;
                from = existing.Provider;
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);

        if (published.IsFailure)
            return Result<ConfigurationMove>.Failure(published.Errors);

        // The entry is published and readable at its new home. Only now is the source withdrawn:
        // the reverse order would risk a withdrawal that succeeded followed by a publish that did
        // not, which destroys the only copy. A stranded copy can be cleaned up; a lost secret cannot.
        var withdrawn = await WithdrawAsync(source!, from, @namespace, key, cancellationToken).ConfigureAwait(false);

        return Result<ConfigurationMove>.Success(new ConfigurationMove
        {
            Namespace = @namespace,
            Key = key,
            From = from,
            To = toProvider,
            SourceWithdrawn = withdrawn,
        });
    }

    private async ValueTask<Result<string>> ReadThroughAsync(
        ConfigurationValue entry,
        string @namespace,
        string key,
        CancellationToken cancellationToken)
    {
        var provider = GetProvider(entry.Provider);
        return provider.IsFailure
            ? Result<string>.Failure(provider.Errors)
            : await provider.Value.ReadAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> WithdrawAsync(
        ConfigurationValue source,
        string from,
        string @namespace,
        string key,
        CancellationToken cancellationToken)
    {
        // A literal had nothing outside the entry, and overwriting the entry withdrew it.
        if (from.Length == 0)
            return true;

        var provider = GetProvider(from);
        if (provider.IsFailure)
        {
            _logger?.LogWarning(
                "Moved {Namespace}/{Key} but provider '{Provider}' is no longer registered, so the source copy remains",
                @namespace, key, from);
            return false;
        }

        var erased = await provider.Value.EraseAsync(source, @namespace, key, cancellationToken).ConfigureAwait(false);
        if (erased.IsSuccess)
            return true;

        _logger?.LogWarning(
            "Moved {Namespace}/{Key} but provider '{Provider}' could not withdraw the source copy ({Category}); a copy remains",
            @namespace, key, from, erased.Errors[0].Category);
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask<Result> RemoveAsync(string @namespace, string key, CancellationToken cancellationToken = default)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result.Failure(document.Errors);

        return await document.Value.ModifyAsync(
            entries =>
            {
                entries.Remove(key);
                return ValueTask.FromResult(Result.Success());
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<IReadOnlyList<string>>> ListAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        var document = _documents.Create(@namespace);
        if (document.IsFailure)
            return Result<IReadOnlyList<string>>.Failure(document.Errors);

        var entries = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        return entries.IsFailure
            ? Result<IReadOnlyList<string>>.Failure(entries.Errors)
            : Result<IReadOnlyList<string>>.Success(
                [.. entries.Value.Keys.OrderBy(name => name, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Reads one value and deserializes it from JSON into <typeparamref name="TValue"/> through
    /// the reflection-based serializer. Callers publishing with Native AOT use
    /// <see cref="GetAsAsync{TValue}(string, string, JsonTypeInfo{TValue}, CancellationToken)"/>.
    /// </summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The entry key; typed methods accept null to select the whole namespace.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <typeparam name="TValue">The type the stored JSON represents.</typeparam>
    [RequiresUnreferencedCode(OpenGenericJustification)]
    [RequiresDynamicCode(OpenGenericJustification)]
    public async ValueTask<Result<TValue>> GetAsAsync<TValue>(string @namespace, string? key, CancellationToken cancellationToken = default)
    {
        var value = key is null
            ? await GetNamespaceAsync(@namespace, cancellationToken).ConfigureAwait(false)
            : await GetAsync(@namespace, key, cancellationToken).ConfigureAwait(false);
        if (value.IsFailure)
            return Result<TValue>.Failure(value.Errors);

        // System.Text.Json throws on malformed input; this is the only place the store touches it,
        // so the translation to a failure happens here rather than escaping to the caller.
        TValue? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<TValue>(value.Value);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Result<TValue>.Failure(Error.FromExceptions(exception, "The configuration value could not be read.", ErrorCategory.Validation));
        }

        return deserialized is null
            ? Result<TValue>.Failure(ValueWasNull(@namespace, key ?? "namespaceSettings"))
            : Result<TValue>.Success(deserialized);
    }

    /// <summary>Reads one value and deserializes it from JSON through a source-generated type description.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The entry key; typed methods accept null to select the whole namespace.</param>
    /// <param name="typeInfo">The source-generated description of <typeparamref name="TValue"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <typeparam name="TValue">The type the stored JSON represents.</typeparam>
    public async ValueTask<Result<TValue>> GetAsAsync<TValue>(string @namespace, string? key, JsonTypeInfo<TValue> typeInfo, CancellationToken cancellationToken = default)
    {
        var value = key is null
            ? await GetNamespaceAsync(@namespace, cancellationToken).ConfigureAwait(false)
            : await GetAsync(@namespace, key, cancellationToken).ConfigureAwait(false);
        if (value.IsFailure)
            return Result<TValue>.Failure(value.Errors);

        TValue? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize(value.Value, typeInfo);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Result<TValue>.Failure(Error.FromExceptions(exception, "The configuration value could not be read.", ErrorCategory.Validation));
        }

        return deserialized is null
            ? Result<TValue>.Failure(ValueWasNull(@namespace, key ?? "namespaceSettings"))
            : Result<TValue>.Success(deserialized);
    }

    /// <summary>
    /// Serializes a value to JSON through the reflection-based serializer and writes it to one
    /// entry. Callers publishing with Native AOT use
    /// <see cref="SetAsAsync{TValue}(string, string, TValue, JsonTypeInfo{TValue}, string, CancellationToken)"/>.
    /// </summary>
    /// <param name="namespace">The namespace to hold the entry.</param>
    /// <param name="key">The entry key; typed methods accept null to select the whole namespace.</param>
    /// <param name="value">The value to serialize and store.</param>
    /// <param name="provider">Where the value is stored; null keeps the existing entry's provider.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <typeparam name="TValue">The type being serialized.</typeparam>
    [RequiresUnreferencedCode(OpenGenericJustification)]
    [RequiresDynamicCode(OpenGenericJustification)]
    public ValueTask<Result> SetAsAsync<TValue>(string @namespace, string? key, TValue value, string? provider = null, CancellationToken cancellationToken = default)
    {
        // System.Text.Json throws for a type it cannot serialize; the boundary is here.
        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(value);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return ValueTask.FromResult(Result.Failure(Error.FromExceptions(exception, "The configuration value could not be stored.", ErrorCategory.Validation)));
        }

        if (key is null)
            return SetNamespaceAsync(@namespace, serialized, cancellationToken);

        return SetValueAsync(@namespace, key, serialized, provider, true, cancellationToken);
    }

    /// <summary>Serializes a value to JSON through a source-generated type description and writes it to one entry.</summary>
    /// <param name="namespace">The namespace to hold the entry.</param>
    /// <param name="key">The entry key; typed methods accept null to select the whole namespace.</param>
    /// <param name="value">The value to serialize and store.</param>
    /// <param name="typeInfo">The source-generated description of <typeparamref name="TValue"/>.</param>
    /// <param name="provider">Where the value is stored; null keeps the existing entry's provider.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <typeparam name="TValue">The type being serialized.</typeparam>
    public ValueTask<Result> SetAsAsync<TValue>(string @namespace, string? key, TValue value, JsonTypeInfo<TValue> typeInfo, string? provider = null, CancellationToken cancellationToken = default)
    {
        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(value, typeInfo);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return ValueTask.FromResult(Result.Failure(Error.FromExceptions(exception, "The configuration value could not be stored.", ErrorCategory.Validation)));
        }

        if (key is null)
            return SetNamespaceAsync(@namespace, serialized, cancellationToken);

        return SetValueAsync(@namespace, key, serialized, provider, true, cancellationToken);
    }

    /// <summary>The failure returned when a stored JSON value deserializes to null.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry.</param>
    public static Error ValueWasNull(string @namespace, string key) => Error.FromMessages(
        "The configuration value could not be read.",
        $"The configuration value at '{@namespace}/{key}' deserialized to null.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a namespace holds no entry under the requested key.</summary>
    /// <param name="namespace">The namespace that was searched.</param>
    /// <param name="key">The key that was not found.</param>
    public static Error EntryNotFound(string @namespace, string key) => Error.FromMessages(
        "The configuration entry was not found.",
        $"No configuration entry exists at '{@namespace}/{key}'.",
        ErrorCategory.NotFound);

    /// <summary>The failure returned when a new value is written without saying where it goes.</summary>
    /// <param name="namespace">The namespace the value would be held in.</param>
    /// <param name="key">The key of the value.</param>
    public static Error DestinationRequired(string @namespace, string key) => Error.FromMessages(
        "A new value needs a destination.",
        $"configuration: '{@namespace}/{key}' does not exist, so there is no stored entry to inherit a provider from; "
        + "pass a provider name, or IConfiguration.Plain to store it unprotected",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a write names a provider other than the entry's own.</summary>
    /// <param name="namespace">The namespace holding the value.</param>
    /// <param name="key">The key of the value.</param>
    /// <param name="provider">The provider the entry already uses.</param>
    public static Error AlreadyStoredElsewhere(string @namespace, string key, string provider) => Error.FromMessages(
        "That setting is stored somewhere else.",
        $"configuration: '{@namespace}/{key}' is held by '{(provider.Length == 0 ? "plain storage" : provider)}'; "
        + "use MoveAsync to change where it lives, which withdraws the copy it leaves behind",
        ErrorCategory.Conflict);

    /// <summary>The failure returned when a move names the provider the value already uses.</summary>
    /// <param name="namespace">The namespace holding the value.</param>
    /// <param name="key">The key of the value.</param>
    /// <param name="provider">The provider named, which is where the value already is.</param>
    public static Error AlreadyThere(string @namespace, string key, string provider) => Error.FromMessages(
        "That setting is already stored there.",
        $"configuration: '{@namespace}/{key}' is already held by '{(provider.Length == 0 ? "plain storage" : provider)}'",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a literal entry is asked to re-authorize.</summary>
    /// <param name="namespace">The namespace holding the entry.</param>
    /// <param name="key">The key of the entry.</param>
    public static Error EntryIsLiteral(string @namespace, string key) => Error.FromMessages(
        "That setting does not sign in.",
        $"The entry at '{@namespace}/{key}' is a literal and has no provider to re-authorize.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the entry's provider cannot re-authorize.</summary>
    /// <param name="name">The name of the provider.</param>
    public static Error ProviderNotReauthorizable(string name) => Error.FromMessages(
        "That setting does not sign in.",
        $"provider '{name}' does not implement IReauthorizableProvider",
        ErrorCategory.Validation);

    /// <summary>The failure returned when an entry names a provider that was never registered.</summary>
    /// <param name="name">The provider name the entry asked for.</param>
    public static Error ProviderNotRegistered(string name) => Error.FromMessages(
        "The configuration entry cannot be resolved.",
        $"Configuration provider '{name}' is not registered.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a provider offered for registration has a blank name.</summary>
    public static Error ProviderNameBlank { get; } = Error.FromMessages(
        "A configuration provider has no name.",
        "Every configuration provider must have a non-blank name.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when two providers are offered under one name.</summary>
    /// <param name="name">The name that was registered twice.</param>
    public static Error ProviderNameDuplicate(string name) => Error.FromMessages(
        "A configuration provider is registered twice.",
        $"Configuration provider '{name}' was registered more than once.",
        ErrorCategory.Conflict);

    /// <summary>The failure returned when a write would store a provider's credential inside that provider.</summary>
    /// <param name="provider">The provider that authenticates with the credential.</param>
    /// <param name="address">The credential's address.</param>
    public static Error CircularCredential(string provider, string address) => Error.FromMessages(
        "That setting cannot be stored there.",
        $"configuration: circular credential: '{address}' is the credential provider '{provider}' authenticates with, "
        + $"so it cannot be stored in '{provider}' — reading the vault would require reading the vault",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a provider's credential has not been acquired yet.</summary>
    /// <param name="provider">The provider that needs it.</param>
    public static Error AuthorizationRequired(string provider) => Error.FromMessages(
        $"Sign in to '{provider}' first.",
        $"configuration: authorization required for provider '{provider}'; no credential has been acquired for it yet",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when a provider named for authorization declares no credential.</summary>
    /// <param name="provider">The provider named.</param>
    public static Error ProviderHasNoCredential(string provider) => Error.FromMessages(
        "That provider does not sign in.",
        $"configuration: provider '{provider}' declares no credential in the manifest",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a provider declares a credential but no way to acquire one.</summary>
    /// <param name="provider">The provider named.</param>
    public static Error ProviderNotAuthorizing(string provider) => Error.FromMessages(
        "That provider does not sign in with OAuth.",
        $"configuration: provider '{provider}' declares a credential but its package describes no authorization server",
        ErrorCategory.Validation);

    /// <summary>The failure returned when no registered provider can run an authorization flow.</summary>
    public static Error NoAuthorizingProvider { get; } = Error.FromMessages(
        "Signing in is not available.",
        "configuration: no registered provider can acquire an OAuth credential",
        ErrorCategory.Validation);

    private Result CheckCircular(string destination, string @namespace, string key) =>
        destination.Length > 0
        && _credentials.TryGetValue(destination, out var credential)
        && string.Equals(credential.Namespace, @namespace, StringComparison.Ordinal)
        && string.Equals(credential.Key, key, StringComparison.Ordinal)
            ? Result.Failure(CircularCredential(destination, credential.Address))
            : Result.Success();

    private Result<IConfigurationProvider> GetAuthorizingProvider()
    {
        var authorizer = _providers.Values.FirstOrDefault(provider => provider is IReauthorizableProvider);
        return authorizer is null
            ? Result<IConfigurationProvider>.Failure(NoAuthorizingProvider)
            : Result<IConfigurationProvider>.Success(authorizer);
    }

    private Result<IConfigurationProvider> GetProvider(string name) =>
        _providers.TryGetValue(name, out var provider)
            ? Result<IConfigurationProvider>.Success(provider)
            : Result<IConfigurationProvider>.Failure(ProviderNotRegistered(name));
}
