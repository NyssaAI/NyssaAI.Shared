using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// ConfigurationStore resolves configuration values, reading literals straight from the document
/// and delegating provider-backed entries to the registered <see cref="IConfigurationProvider"/>.
/// </summary>
internal sealed class ConfigurationStore : IConfigurationValues
{
    private const string OpenGenericJustification =
        "TValue is caller-supplied and cannot be enumerated into a source-generated context at this project's compile time; "
        + "callers publishing with Native AOT use the overload that takes a JsonTypeInfo<TValue>.";

    private readonly ConfigurationProviderRegistry _registry;
    private readonly ConfigurationRecoveryCoordinator _recoveryCoordinator;
    private readonly ConfigurationEntryHandler _entryHandler;
    private readonly NamespaceDocumentHandler _namespaceHandler;

    /// <summary>Creates a store over one document factory with no providers; every entry is a literal.</summary>
    /// <param name="documents">The factory that opens a namespace's document.</param>
    /// <param name="logger">The logger failures are reported to, if any.</param>
    public ConfigurationStore(IConfigurationDocumentFactory documents, ILogger? logger = null)
        : this(
            documents,
            ConfigurationProviderRegistry.Create(documents, [], [], logger).Value,
            logger)
    {
    }

    private ConfigurationStore(
        IConfigurationDocumentFactory documents,
        ConfigurationProviderRegistry registry,
        ILogger? logger,
        IRecovery? recovery = null)
    {
        _registry = registry;
        _recoveryCoordinator = new ConfigurationRecoveryCoordinator(documents, registry.GetProvider, recovery);
        _entryHandler = new ConfigurationEntryHandler(documents, registry, _recoveryCoordinator, logger);
        _namespaceHandler = new NamespaceDocumentHandler(
            documents,
            _entryHandler.ReadThroughAsync,
            registry.GetProvider,
            registry.CheckCircular,
            _recoveryCoordinator);
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
    /// <param name="recovery">The application-scoped recovery handle protector, when composed.</param>
    public static Result<ConfigurationStore> Create(
        IConfigurationDocumentFactory documents,
        IEnumerable<IConfigurationProvider> providers,
        IEnumerable<ProviderCredential> credentials,
        ILogger? logger = null,
        IRecovery? recovery = null)
    {
        var registry = ConfigurationProviderRegistry.Create(documents, providers, credentials, logger);
        return registry.IsFailure
            ? Result<ConfigurationStore>.Failure(registry.Errors)
            : Result<ConfigurationStore>.Success(new ConfigurationStore(documents, registry.Value, logger, recovery));
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
    public ValueTask<Result<string>> ResolveCredentialAsync(string provider, CancellationToken cancellationToken = default) =>
        _registry.ResolveCredentialAsync(provider, GetAsync, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result> AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken = default) =>
        _registry.AuthorizeAsync(@namespace, key, options, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result> AuthorizeProviderAsync(string provider, CancellationToken cancellationToken = default) =>
        _registry.AuthorizeProviderAsync(provider, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result<string>> GetAsync(string @namespace, string key, CancellationToken cancellationToken = default) =>
        _entryHandler.ReadValueAsync(@namespace, key, encodeString: false, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result> SetAsync(
        string @namespace,
        string key,
        string value,
        string? provider = null,
        CancellationToken cancellationToken = default) =>
        _entryHandler.SetValueAsync(@namespace, key, value, provider, false, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result> ReauthorizeAsync(string @namespace, string key, CancellationToken cancellationToken = default) =>
        _registry.ReauthorizeAsync(@namespace, key, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ConfigurationAttempt<ConfigurationMove>> MoveAsync(
        string @namespace, string key, string? toProvider = null, CancellationToken cancellationToken = default) =>
        _entryHandler.MoveAsync(@namespace, key, toProvider, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ConfigurationAttempt<ConfigurationRecoveryOutcome>> RecoverAsync(
        ConfigurationRecoveryHandle handle, CancellationToken cancellationToken = default) =>
        _recoveryCoordinator.RecoverAsync(handle, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result<ConfigurationRemoval>> RemoveAsync(string @namespace, string key, CancellationToken cancellationToken = default) =>
        _entryHandler.RemoveAsync(@namespace, key, ResolveCredentialAsync, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<Result<IReadOnlyList<string>>> ListAsync(string @namespace, CancellationToken cancellationToken = default) =>
        _entryHandler.ListAsync(@namespace, cancellationToken);

    /// <summary>
    /// Reads one value and deserializes it from JSON into <typeparamref name="TValue"/> through
    /// the reflection-based serializer. Callers publishing with Native AOT use
    /// <see cref="GetAsAsync{TValue}(string, string?, JsonTypeInfo{TValue}, CancellationToken)"/>.
    /// </summary>
    [RequiresUnreferencedCode(OpenGenericJustification)]
    [RequiresDynamicCode(OpenGenericJustification)]
    public async ValueTask<Result<TValue>> GetAsAsync<TValue>(string @namespace, string? key, CancellationToken cancellationToken = default)
    {
        var value = await ReadJsonSourceAsync(@namespace, key, typeof(TValue) == typeof(string), cancellationToken).ConfigureAwait(false);
        return value.IsFailure
            ? Result<TValue>.Failure(value.Errors)
            : Deserialize(@namespace, key, () => JsonSerializer.Deserialize<TValue>(value.Value));
    }

    /// <summary>Reads one value and deserializes it from JSON through a source-generated type description.</summary>
    public async ValueTask<Result<TValue>> GetAsAsync<TValue>(string @namespace, string? key, JsonTypeInfo<TValue> typeInfo, CancellationToken cancellationToken = default)
    {
        var value = await ReadJsonSourceAsync(@namespace, key, typeof(TValue) == typeof(string), cancellationToken).ConfigureAwait(false);
        return value.IsFailure
            ? Result<TValue>.Failure(value.Errors)
            : Deserialize(@namespace, key, () => JsonSerializer.Deserialize(value.Value, typeInfo));
    }

    /// <summary>
    /// Serializes a value to JSON through the reflection-based serializer and writes it to one
    /// entry. Callers publishing with Native AOT use
    /// <see cref="SetAsAsync{TValue}(string, string?, TValue, JsonTypeInfo{TValue}, string?, CancellationToken)"/>.
    /// </summary>
    [RequiresUnreferencedCode(OpenGenericJustification)]
    [RequiresDynamicCode(OpenGenericJustification)]
    public ValueTask<ConfigurationAttempt> SetAsAsync<TValue>(string @namespace, string? key, TValue value, string? provider = null, CancellationToken cancellationToken = default) =>
        WriteSerializedAsync(@namespace, key, () => JsonSerializer.Serialize(value), provider, replaceNamespace: true, cancellationToken);

    /// <inheritdoc/>
    [RequiresUnreferencedCode(OpenGenericJustification)]
    [RequiresDynamicCode(OpenGenericJustification)]
    public ValueTask<ConfigurationAttempt> MergeSettingsAsync<TValue>(string @namespace, TValue value, CancellationToken cancellationToken = default) =>
        WriteSerializedAsync(@namespace, null, () => JsonSerializer.Serialize(value), null, replaceNamespace: false, cancellationToken);

    /// <summary>Serializes a value to JSON through a source-generated type description and writes it to one entry.</summary>
    public ValueTask<ConfigurationAttempt> SetAsAsync<TValue>(string @namespace, string? key, TValue value, JsonTypeInfo<TValue> typeInfo, string? provider = null, CancellationToken cancellationToken = default) =>
        WriteSerializedAsync(@namespace, key, () => JsonSerializer.Serialize(value, typeInfo), provider, replaceNamespace: true, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<ConfigurationAttempt> MergeSettingsAsync<TValue>(string @namespace, JsonTypeInfo<TValue> typeInfo, TValue value, CancellationToken cancellationToken = default) =>
        WriteSerializedAsync(@namespace, null, () => JsonSerializer.Serialize(value, typeInfo), null, replaceNamespace: false, cancellationToken);

    private ValueTask<Result<string>> ReadJsonSourceAsync(string @namespace, string? key, bool encodeString, CancellationToken cancellationToken) =>
        key is null
            ? _namespaceHandler.GetNamespaceAsync(@namespace, cancellationToken)
            : _entryHandler.ReadValueAsync(@namespace, key, encodeString, cancellationToken);

    private static Result<TValue> Deserialize<TValue>(string @namespace, string? key, Func<TValue?> deserialize)
    {
        TValue? deserialized;
        try
        {
            deserialized = deserialize();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Result<TValue>.Failure(Error.FromExceptions(exception, "The configuration value could not be read.", ErrorCategory.Validation));
        }

        return deserialized is null
            ? Result<TValue>.Failure(ValueWasNull(@namespace, key ?? "namespaceSettings"))
            : Result<TValue>.Success(deserialized);
    }

    private ValueTask<ConfigurationAttempt> WriteSerializedAsync(
        string @namespace, string? key, Func<string> serialize, string? provider, bool replaceNamespace, CancellationToken cancellationToken)
    {
        string serialized;
        try
        {
            serialized = serialize();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return ValueTask.FromResult<ConfigurationAttempt>(Result.Failure(Error.FromExceptions(exception, "The configuration value could not be stored.", ErrorCategory.Validation)));
        }

        return key is null
            ? _namespaceHandler.SetNamespaceAsync(@namespace, serialized, replaceNamespace, cancellationToken)
            : ConfigurationAttempt.FromAsync(_entryHandler.SetValueAsync(@namespace, key, serialized, provider, true, cancellationToken));
    }

    /// <summary>The failure returned when a stored JSON value deserializes to null.</summary>
    public static Error ValueWasNull(string @namespace, string key) => Error.FromMessages(
        "The configuration value could not be read.",
        $"The configuration value at '{@namespace}/{key}' deserialized to null.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a namespace holds no entry under the requested key.</summary>
    public static Error EntryNotFound(string @namespace, string key) => Error.FromMessages(
        "The configuration entry was not found.",
        $"No configuration entry exists at '{@namespace}/{key}'.",
        ErrorCategory.NotFound);

    /// <summary>The failure returned when a write names a provider other than the entry's own.</summary>
    public static Error AlreadyStoredElsewhere(string @namespace, string key, string provider) => Error.FromMessages(
        "That setting is stored somewhere else.",
        $"configuration: '{@namespace}/{key}' is held by '{(provider.Length == 0 ? "plain storage" : provider)}'; "
        + "use MoveAsync to change where it lives, which withdraws the copy it leaves behind",
        ErrorCategory.Conflict);

    /// <summary>The failure returned when a move names the provider the value already uses.</summary>
    public static Error AlreadyThere(string @namespace, string key, string provider) => Error.FromMessages(
        "That setting is already stored there.",
        $"configuration: '{@namespace}/{key}' is already held by '{(provider.Length == 0 ? "plain storage" : provider)}'",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a literal entry is asked to re-authorize.</summary>
    public static Error EntryIsLiteral(string @namespace, string key) => Error.FromMessages(
        "That setting does not sign in.",
        $"The entry at '{@namespace}/{key}' is a literal and has no provider to re-authorize.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the entry's provider cannot re-authorize.</summary>
    public static Error ProviderNotReauthorizable(string name) => Error.FromMessages(
        "That setting does not sign in.",
        $"provider '{name}' does not implement IReauthorizableProvider",
        ErrorCategory.Validation);

    /// <summary>The failure returned when an entry names a provider that was never registered.</summary>
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
    public static Error ProviderNameDuplicate(string name) => Error.FromMessages(
        "A configuration provider is registered twice.",
        $"Configuration provider '{name}' was registered more than once.",
        ErrorCategory.Conflict);

    /// <summary>The failure returned when a write would store a provider's credential inside that provider.</summary>
    public static Error CircularCredential(string provider, string address) => Error.FromMessages(
        "That setting cannot be stored there.",
        $"configuration: circular credential: '{address}' is the credential provider '{provider}' authenticates with, "
        + $"so it cannot be stored in '{provider}' — reading the vault would require reading the vault",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a provider's credential has not been acquired yet.</summary>
    public static Error AuthorizationRequired(string provider) => Error.FromMessages(
        $"Sign in to '{provider}' first.",
        $"configuration: authorization required for provider '{provider}'; no credential has been acquired for it yet",
        ErrorCategory.Forbidden);

    /// <summary>The failure returned when a provider named for authorization declares no credential.</summary>
    public static Error ProviderHasNoCredential(string provider) => Error.FromMessages(
        "That provider does not sign in.",
        $"configuration: provider '{provider}' declares no credential in the manifest",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a provider declares a credential but no way to acquire one.</summary>
    public static Error ProviderNotAuthorizing(string provider) => Error.FromMessages(
        "That provider does not sign in with OAuth.",
        $"configuration: provider '{provider}' declares a credential but its package describes no authorization server",
        ErrorCategory.Validation);

    /// <summary>The failure returned when no registered provider can run an authorization flow.</summary>
    public static Error NoAuthorizingProvider { get; } = Error.FromMessages(
        "Signing in is not available.",
        "configuration: no registered provider can acquire an OAuth credential",
        ErrorCategory.Validation);
}
