using Microsoft.Extensions.Logging;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// Manages registered <see cref="IConfigurationProvider"/> instances, provider credentials,
/// circular-credential checks, and OAuth authorization/reauthorization workflows.
/// </summary>
internal sealed class ConfigurationProviderRegistry
{
    private readonly IConfigurationDocumentFactory _documents;
    private readonly IReadOnlyDictionary<string, IConfigurationProvider> _providers;
    private readonly IReadOnlyDictionary<string, ProviderCredential> _credentials;
    private readonly ILogger? _logger;
    private readonly AsyncLocal<(string Provider, string Token)?> _removalCredential = new();

    private ConfigurationProviderRegistry(
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

    public static Result<ConfigurationProviderRegistry> Create(
        IConfigurationDocumentFactory documents,
        IEnumerable<IConfigurationProvider> providers,
        IEnumerable<ProviderCredential> credentials,
        ILogger? logger)
    {
        var registered = new Dictionary<string, IConfigurationProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
                return Result<ConfigurationProviderRegistry>.Failure(ConfigurationStore.ProviderNameBlank);

            if (!registered.TryAdd(provider.Name, provider))
                return Result<ConfigurationProviderRegistry>.Failure(ConfigurationStore.ProviderNameDuplicate(provider.Name));
        }

        var declared = new Dictionary<string, ProviderCredential>(StringComparer.Ordinal);
        foreach (var credential in credentials)
        {
            if (!registered.ContainsKey(credential.Provider))
                return Result<ConfigurationProviderRegistry>.Failure(ConfigurationStore.ProviderNotRegistered(credential.Provider));

            declared[credential.Provider] = credential;
        }

        return Result<ConfigurationProviderRegistry>.Success(
            new ConfigurationProviderRegistry(documents, registered, declared, logger));
    }

    public bool HasCredential(string provider) => _credentials.ContainsKey(provider);

    public Result<IConfigurationProvider> GetProvider(string name) =>
        _providers.TryGetValue(name, out var provider)
            ? Result<IConfigurationProvider>.Success(provider)
            : Result<IConfigurationProvider>.Failure(ConfigurationStore.ProviderNotRegistered(name));

    public Result<IConfigurationProvider> GetAuthorizingProvider()
    {
        var authorizer = _providers.Values.FirstOrDefault(provider => provider is IReauthorizableProvider);
        return authorizer is null
            ? Result<IConfigurationProvider>.Failure(ConfigurationStore.NoAuthorizingProvider)
            : Result<IConfigurationProvider>.Success(authorizer);
    }

    public Result CheckCircular(string destination, string @namespace, string key) =>
        destination.Length > 0
        && _credentials.TryGetValue(destination, out var credential)
        && string.Equals(credential.Namespace, @namespace, StringComparison.Ordinal)
        && string.Equals(credential.Key, key, StringComparison.Ordinal)
            ? Result.Failure(ConfigurationStore.CircularCredential(destination, credential.Address))
            : Result.Success();

    public async ValueTask<Result<string>> ResolveCredentialAsync(
        string provider,
        Func<string, string, CancellationToken, ValueTask<Result<string>>> getValueAsync,
        CancellationToken cancellationToken)
    {
        // Removal prepares its token before taking the document lock. Only that async operation
        // may reuse it; asking the document again here would acquire the same lock recursively.
        if (_removalCredential.Value is { } prepared && prepared.Provider == provider)
            return cancellationToken.IsCancellationRequested
                ? Result<string>.Failure(Error.Canceled(new OperationCanceledException(cancellationToken)))
                : Result<string>.Success(prepared.Token);

        if (!_credentials.TryGetValue(provider, out var credential))
            return Result<string>.Failure(ConfigurationStore.ProviderHasNoCredential(provider));

        var document = _documents.Create(credential.Namespace);
        if (document.IsFailure)
            return Result<string>.Failure(document.Errors);

        var entries = await document.Value.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (entries.IsFailure)
            return Result<string>.Failure(entries.Errors);

        if (!entries.Value.TryGetValue(credential.Key, out var entry))
            return Result<string>.Failure(ConfigurationStore.AuthorizationRequired(provider));

        // Checked on the envelope, before anything is opened: resolving the value would already
        // be the circular call.
        if (string.Equals(entry.Provider, provider, StringComparison.Ordinal))
            return Result<string>.Failure(ConfigurationStore.CircularCredential(provider, credential.Address));

        return await getValueAsync(credential.Namespace, credential.Key, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Result<ConfigurationRemoval>> EraseWithPreparedCredentialAsync(
        IConfigurationProvider provider,
        ConfigurationValue entry,
        string @namespace,
        string key,
        (string Provider, string Token)? preparedCredential,
        CancellationToken cancellationToken)
    {
        var previousCredential = _removalCredential.Value;
        _removalCredential.Value = preparedCredential;
        try
        {
            return await provider.EraseAsync(entry, @namespace, key, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _removalCredential.Value = previousCredential;
        }
    }

    public async ValueTask<Result> AuthorizeAsync(
        string @namespace,
        string key,
        OAuthOptions options,
        CancellationToken cancellationToken)
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
                    return Result.Failure(ConfigurationStore.AlreadyStoredElsewhere(@namespace, key, existing.Provider));

                var authorized = await ((IReauthorizableProvider)authorizer.Value)
                    .AuthorizeAsync(@namespace, key, options, cancellationToken).ConfigureAwait(false);
                if (authorized.IsFailure)
                    return Result.Failure(authorized.Errors);

                entries[key] = authorized.Value;
                return Result.Success();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<Result> AuthorizeProviderAsync(string provider, CancellationToken cancellationToken)
    {
        if (!_credentials.TryGetValue(provider, out var credential))
            return ValueTask.FromResult(Result.Failure(ConfigurationStore.ProviderHasNoCredential(provider)));

        if (credential.Authorization is null)
            return ValueTask.FromResult(Result.Failure(ConfigurationStore.ProviderNotAuthorizing(provider)));

        return AuthorizeAsync(credential.Namespace, credential.Key, credential.Authorization, cancellationToken);
    }

    public async ValueTask<Result> ReauthorizeAsync(string @namespace, string key, CancellationToken cancellationToken)
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
                    return Result.Failure(ConfigurationStore.EntryNotFound(@namespace, key));

                if (entry.IsLiteral)
                    return Result.Failure(ConfigurationStore.EntryIsLiteral(@namespace, key));

                var provider = GetProvider(entry.Provider);
                if (provider.IsFailure)
                    return Result.Failure(provider.Errors);

                if (provider.Value is not IReauthorizableProvider reauthorizable)
                    return Result.Failure(ConfigurationStore.ProviderNotReauthorizable(entry.Provider));

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
}
