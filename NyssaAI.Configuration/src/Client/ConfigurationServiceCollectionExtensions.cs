using NyssaAI.Configuration.Recovery;
using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.Manifest;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Configuration.OAuth.Infrastructure;
using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Configuration.Values;

namespace NyssaAI.Configuration.Client;

/// <summary>Registers Nyssa configuration for one application.</summary>
/// <remarks>
/// <para>
/// One call, three shapes: nothing, a <see cref="NyssaConfigurationOptions"/> instance, or a
/// delegate that edits one. Nothing is the ordinary shape:
/// <see cref="NyssaConfigurationOptions"/> arrives with every value already set — the application
/// named by its entry assembly, its configuration directory beneath the application's base directory, the
/// user's manifest beneath the profile directory — so which directory, which vaults, and whether
/// encryption is on are decided by its manifest section and by this package.
/// </para>
/// <para>
/// Every registration is scoped and the first one wins. Resolving <see cref="IConfiguration"/> is
/// what opens the store, so a machine whose manifest cannot be read fails there, at startup, with
/// the path and the section in the message. The application may replace the credential store, the
/// clock, the browser, and the HTTP client by registering <see cref="IKeyring"/>,
/// <see cref="TimeProvider"/>, <see cref="IBrowserLauncher"/>, and an <c>IHttpClientFactory</c>;
/// a vault package registers its <see cref="ICloudKeyVaultFactory"/>. Everything an application
/// could substitute is resolved from the container by its framework type, so a test replaces the
/// OS without a single Nyssa seam.
/// </para>
/// </remarks>
public static class ConfigurationServiceCollectionExtensions
{
    /// <summary>The keyring service name under which every application's local encryption key is filed.</summary>
    public const string KeyringService = "nyssa-ai-configuration";

    private const int KeyLength = 32;

    /// <summary>Registers Nyssa configuration with every default: the entry assembly's name, the application's base directory, the user's manifest.</summary>
    /// <param name="services">The container to register with.</param>
    public static IServiceCollection AddNyssaConfiguration(this IServiceCollection services) =>
        services.AddNyssaConfiguration(new NyssaConfigurationOptions());

    /// <summary>Registers Nyssa configuration, letting the caller edit the startup options before they are used.</summary>
    /// <param name="services">The container to register with.</param>
    /// <param name="configure">Edits a fresh <see cref="NyssaConfigurationOptions"/> whose every value is already its default.</param>
    public static IServiceCollection AddNyssaConfiguration(this IServiceCollection services, Action<NyssaConfigurationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NyssaConfigurationOptions();
        configure(options);
        return services.AddNyssaConfiguration(options);
    }

    /// <summary>
    /// Registers Nyssa configuration from a prepared <see cref="NyssaConfigurationOptions"/>, and
    /// is where the configuration root is composed: the manifest section, the encryption key, the
    /// providers, and the store over them. This is the only place in the package that names
    /// concrete types across features.
    /// </summary>
    /// <remarks>
    /// The manifest section is the bootstrap: it gates which configuration directory is consulted
    /// at all, and names the key vault. An application that has never been set up has no section,
    /// and one is provisioned for it — naming the directory its options carry — so referencing the
    /// package and registering it is the whole of what a developer must do. Two limits on that:
    /// only absence is filled in, so a manifest that exists but cannot be read, or is malformed,
    /// still fails; and only a manifest at the conventional
    /// <c>&lt;home&gt;/.nyssa-ai/config-manifest.json</c> is written, because a manifest the
    /// deployment located is the deployment's to place, and its absence is that deployment's
    /// failure to report. The encryption key is likewise created when it is absent, and both
    /// creations are logged.
    /// </remarks>
    /// <param name="services">The container to register with.</param>
    /// <param name="options">The startup options. The instance is held for the lifetime of the registration, so a caller that keeps editing it edits what startup will read.</param>
    public static IServiceCollection AddNyssaConfiguration(this IServiceCollection services, NyssaConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // The startup options themselves, so an application — or a test — can see what the
        // library decided: which name it took from the entry assembly, and the two paths.
        services.TryAddSingleton(options);

        services.TryAddScoped<ConfigurationClient>(provider =>
        {
            var applicationName = options.ApplicationName;
            var manifestPath = options.Bootstrap.ConfigManifest;
            var logger = provider.GetService<ILoggerFactory>()?.CreateLogger("NyssaAI.Configuration");
            var clock = provider.GetService<TimeProvider>() ?? TimeProvider.System;
            var keyring = provider.GetService<IKeyring>() ?? KeyringDefault.Create();

            // 1. The manifest section: the directory and the key vault. What it says is layered over
            //    what the developer said in code. An absent section is the machine nobody has set up,
            //    and the default is written for it.
            var section = ConfigurationManifestReader.ReadApplication(manifestPath, applicationName);
            if (section.IsFailure && section.Errors[0].Category == ErrorCategory.NotFound)
                section = ProvisionSection(manifestPath, applicationName, options.ConfigDirectory, options.Bootstrap.LockTimeout, logger);

            if (section.IsFailure)
                throw Failed(section.Errors);

            var keyVault = OverlayKeyVault(options.Bootstrap.KeyVault, section.Value.KeyVault);
            if (keyVault.IsFailure)
                throw Failed(keyVault.Errors);

            // 2. The configuration directory, protected to the current account if created here.
            var directory = section.Value.ConfigurationDirectory;
            var created = ProtectedFileSystem.CreateDirectory(directory);
            if (created.IsFailure)
                throw Failed(created.Errors);

            // 3. The encryption key. This is the only write the library ever makes to the credential
            //    store, and it happens once: when the key is absent.
            var key = GetOrCreateKey(applicationName, keyring, logger);
            if (key.IsFailure)
                throw Failed(key.Errors);

            IEncryption encryption = new LocalEncryption();
            var cipher = encryption.OpenCipher(key.Value);
            IRecovery recovery = new ConfigurationRecovery(key.Value, applicationName, directory);
            CryptographicOperations.ZeroMemory(key.Value);
            if (cipher.IsFailure)
                throw Failed(cipher.Errors);

            // 4. Providers: aesLocal and oauth always; one more when the section or the code names a vault.
            IValues values = new LocalValues();
            var documents = values.OpenDocuments(directory, section.Value.LockTimeout ?? options.Bootstrap.LockTimeout);
            var owned = new Owned();
            var client = provider.GetService<IHttpClientFactory>()?.CreateClient()
                ?? owned.Add(new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }, disposeHandler: true));
            var browser = provider.GetService<IBrowserLauncher>() ?? new BrowserLauncherShell();

            var providers = new List<IConfigurationProvider>
            {
                encryption.OpenProvider(cipher.Value),
                new OAuthTokenProvider(cipher.Value, documents, client, browser, clock, logger),
            };

            var storeReference = new StoreReference();
            var credentials = new List<ProviderCredential>();
            var built = BuildKeyVault(keyVault.Value, provider, storeReference, providers, credentials);
            if (built.IsFailure)
                throw Failed(built.Errors);

            // 5. The store.
            var store = ConfigurationStore.Create(documents, providers, credentials, logger, recovery);
            if (store.IsFailure)
                throw Failed(store.Errors);

            storeReference.Current = store.Value;
            return new ConfigurationClient(store.Value, owned);
        });

        services.TryAddScoped<IConfiguration>(provider => provider.GetRequiredService<ConfigurationClient>());
        services.TryAddScoped<IConfigurationReader>(provider => provider.GetRequiredService<ConfigurationClient>());
        services.TryAddScoped<IConfigurationWriter>(provider => provider.GetRequiredService<ConfigurationClient>());
        services.TryAddScoped<IConfigurationMaintainer>(provider => provider.GetRequiredService<ConfigurationClient>());
        services.TryAddScoped<IConfigurationOAuthWriter>(provider => provider.GetRequiredService<ConfigurationClient>());
        services.TryAddScoped<IConfigurationExtensions>(provider => provider.GetRequiredService<ConfigurationClient>());

        return services;
    }

    // What a failure anywhere in the composition becomes: the resolve that asked for the
    // configuration fails, naming every technical message, because there is no configuration to
    // hand back and no honest way to carry on without one.
    private static InvalidOperationException Failed(IReadOnlyList<Error> errors) =>
        new(string.Join("; ", errors.Select(error => error.TechnicalMessage)));

    private static Result<byte[]> GetOrCreateKey(string applicationName, IKeyring keyring, ILogger? logger)
    {
        var key = KeyringManagedSecret.GetOrCreate(KeyringService, applicationName, KeyLength, keyring);
        if (key.IsFailure)
            return Result<byte[]>.Failure(KeyUnavailable(applicationName, key.Errors[0]));

        if (key.Value.Created)
        {
            logger?.LogInformation(
                "Created encryption key for {Tool}: none was in the credential store; "
                + "the key is filed at keyring service '{Service}', entry '{Entry}'",
                applicationName, KeyringService, applicationName);
        }

        return Result<byte[]>.Success(key.Value.Material);
    }

    // Writes the section an application that has never been set up would have had, and reads it
    // back. The directory recorded is the one the options carry, so what the developer defaulted
    // to — or chose — is what the manifest ends up gating. The write is the manifest writer's
    // ordinary one: taken under the manifest lock and published atomically, so two processes
    // starting at once do not lose each other's sections.
    private static Result<ApplicationManifestSettings> ProvisionSection(
        string manifestPath,
        string applicationName,
        string configurationDirectory,
        TimeSpan? lockTimeout,
        ILogger? logger)
    {
        var recorded = ConfigurationManifestWriter.SetConfigurationDirectory(
            manifestPath, applicationName, configurationDirectory, lockTimeout);

        if (recorded.IsFailure)
            return Result<ApplicationManifestSettings>.Failure(recorded.Errors);

        logger?.LogInformation(
            "Created configuration manifest section for {ApplicationName} in {ManifestPath}, naming directory {ConfigurationDirectory}",
            applicationName, manifestPath, recorded.Value);

        return ConfigurationManifestReader.ReadApplication(manifestPath, applicationName);
    }

    // The developer's vault is the default; the section's replaces it whole, because an operator
    // who names a vault owns everything about it. Nothing here logs an entry: a manifest vault may
    // carry secrets.
    private static Result<VaultOptions?> OverlayKeyVault(VaultOptions? fromCode, VaultManifestEntry? fromManifest)
    {
        if (fromManifest is null)
            return Result<VaultOptions?>.Success(fromCode);

        var name = fromManifest.Name;
        Uri? endpoint = null;
        if (fromManifest.Endpoint is not null && !Uri.TryCreate(fromManifest.Endpoint, UriKind.Absolute, out endpoint))
            return Result<VaultOptions?>.Failure(VaultEndpointInvalid(name, fromManifest.Endpoint));

        if (fromManifest.Credential is not null && ConfigurationPointer.Parse(name, fromManifest.Credential).IsFailure)
            return Result<VaultOptions?>.Failure(VaultCredentialInvalid(name, fromManifest.Credential));

        var vault = new VaultOptions
        {
            Name = name,
            Kind = fromManifest.Kind,
            Endpoint = endpoint,
            Credential = fromManifest.Credential,
        };

        foreach (var (key, value) in fromManifest.Settings!)
            vault.Settings[key] = value;
        foreach (var (key, value) in fromManifest.Secrets!)
            vault.Secrets[key] = value;

        return Result<VaultOptions?>.Success(vault);
    }

    private static Result BuildKeyVault(
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
            return Result.Failure(VaultKindUnregistered(name, entry.Kind, factories.Keys));

        IAccessTokenSource? tokens = null;
        if (entry.Credential is not null)
        {
            var address = ConfigurationPointer.Parse(name, entry.Credential);
            if (address.IsFailure)
                return Result.Failure(VaultCredentialInvalid(name, entry.Credential));

            var authorization = factory.Authorization(entry);
            if (authorization is null)
                return Result.Failure(ConfigurationStore.ProviderNotAuthorizing(name));

            tokens = new AccessTokenSource(name, storeReference);
            credentials.Add(new ProviderCredential
            {
                Provider = name,
                Namespace = address.Value.Namespace,
                Key = address.Value.Key,
                Authorization = authorization,
            });
        }

        var built = factory.Create(entry, tokens);
        if (built.IsFailure)
            return Result.Failure(built.Errors);

        providers.Add(new CloudKeyVaultProvider(built.Value, name));
        return Result.Success();
    }

    /// <summary>The failure returned when the credential store could not supply or accept the key.</summary>
    private static Error KeyUnavailable(string applicationName, Error cause) => Error.FromMessages(
        "The application's encryption key is not available.",
        $"configuration: the encryption key for '{applicationName}' at keyring service '{KeyringService}', entry '{applicationName}' "
        + $"could not be obtained: {cause.TechnicalMessage}; the application needs a reachable credential store",
        cause.Category);

    /// <summary>The failure returned when the manifest names a vault kind nobody registered.</summary>
    private static Error VaultKindUnregistered(string vault, string kind, IEnumerable<string> registered) => Error.FromMessages(
        "A configured vault is not available in this application.",
        $"manifest: vault '{vault}' is of kind '{kind}', but no ICloudKeyVaultFactory of that kind is registered; "
        + $"registered kinds: {(registered.Any() ? string.Join(", ", registered.Order(StringComparer.Ordinal)) : "none")}",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a vault's endpoint is not an absolute URI.</summary>
    private static Error VaultEndpointInvalid(string vault, string endpoint) => Error.FromMessages(
        "The configuration manifest is malformed.",
        $"manifest: vault '{vault}' names endpoint '{endpoint}', which is not an absolute URI",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a vault's credential is not a <c>namespace/key</c> address.</summary>
    private static Error VaultCredentialInvalid(string vault, string credential) => Error.FromMessages(
        "The configuration manifest is malformed.",
        $"manifest: vault '{vault}' names credential '{credential}', which is not a namespace/key address",
        ErrorCategory.Validation);

    // What the composition opened and the resolving scope must release: only the HTTP client this
    // package created for itself. A client from IHttpClientFactory is the factory's to manage.
    private sealed class Owned : IDisposable
    {
        private readonly List<IDisposable> _items = [];

        public T Add<T>(T item) where T : IDisposable
        {
            _items.Add(item);
            return item;
        }

        public void Dispose()
        {
            foreach (var item in _items)
                item.Dispose();
        }
    }
}
