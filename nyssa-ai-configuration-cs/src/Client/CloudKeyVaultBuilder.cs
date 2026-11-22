using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.CloudKeyVaults.Bitwarden;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Values;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// Resolves <see cref="ICloudKeyVaultFactory"/> implementations, wires optional OAuth token
/// credentials, applies caching, and registers <see cref="CloudKeyVaultProvider"/> instances.
/// </summary>
internal static class CloudKeyVaultBuilder
{
    internal static Result Build(
        VaultOptions? entry,
        IServiceProvider services,
        StoreReference storeReference,
        List<IConfigurationProvider> providers,
        List<ProviderCredential> credentials)
    {
        if (entry is null)
            return Result.Success();

        var name = entry.Name;
        var factories = services.GetServices<ICloudKeyVaultFactory>()
            .GroupBy(factory => factory.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (!factories.TryGetValue(entry.Kind, out var factory))
        {
            if (entry.Kind is not (CloudKeyVaultFactoryBitwarden.DefaultKind or CloudKeyVaultFactoryBitwarden.ShortKind or CloudKeyVaultFactoryBitwarden.LegacyKind))
                return Result.Failure(VaultKindUnregistered(name, entry.Kind, factories.Keys));
            factory = new CloudKeyVaultFactoryBitwarden(entry.Kind);
        }

        IAccessTokenSource? tokens = null;
        if (entry.Credential is not null)
        {
            var address = ConfigurationPointer.Parse(name, entry.Credential);
            if (address.IsFailure)
                return Result.Failure(ManifestVaultResolver.VaultCredentialInvalid(name, entry.Credential));
            var authorization = factory.Authorization(entry);
            if (authorization is null)
                return Result.Failure(ConfigurationStore.ProviderNotAuthorizing(name));
            tokens = new AccessTokenSource(name, storeReference);
            credentials.Add(new ProviderCredential
            {
                Provider = name, Namespace = address.Value.Namespace, Key = address.Value.Key, Authorization = authorization,
            });
        }

        var built = factory.Create(entry, tokens);
        if (built.IsFailure)
            return Result.Failure(built.Errors);

        var wrapped = CloudKeyVaultCaching.Wrap(
            built.Value,
            entry,
            services.GetService<ISecretCache>(),
            services.GetService<TimeProvider>());
        if (wrapped.IsFailure)
            return Result.Failure(wrapped.Errors);

        providers.Add(new CloudKeyVaultProvider(wrapped.Value, name));
        return Result.Success();
    }

    private static Error VaultKindUnregistered(string vault, string kind, IEnumerable<string> registered) => Error.FromMessages(
        "A configured vault is not available in this application.",
        $"manifest: vault '{vault}' is of kind '{kind}', but no ICloudKeyVaultFactory of that kind is registered; "
        + $"registered kinds: {(registered.Any() ? string.Join(", ", registered.Order(StringComparer.Ordinal)) : "none")}",
        ErrorCategory.Validation);
}
