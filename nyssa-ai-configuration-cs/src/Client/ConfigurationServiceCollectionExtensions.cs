using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.CloudKeyVaults.Bitwarden;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Configuration.Manifest;
using NyssaAI.Configuration.OAuth.Infrastructure;
using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Configuration.Values;

namespace NyssaAI.Configuration.Client;

/// <summary>Registers Nyssa configuration for one application and manages shared manifest key vaults.</summary>
/// <remarks>
/// Every registration is scoped and the first one wins. Resolving <see cref="IConfiguration"/> opens
/// the store, so an unreadable manifest fails at startup naming the path and section. Applications
/// may replace <see cref="IKeyring"/>, <see cref="ISecretCache"/>, <see cref="TimeProvider"/>,
/// <see cref="IBrowserLauncher"/>, <c>IHttpClientFactory</c>, and <see cref="ICloudKeyVaultFactory"/>.
/// </remarks>
public static class ConfigurationServiceCollectionExtensions
{
    /// <summary>The keyring service name under which local and manifest encryption keys are filed.</summary>
    public const string KeyringService = "nyssa-ai-configuration";

    /// <summary>The keyring account name under which the shared manifest secret encryption key is filed.</summary>
    public const string ManifestKeyAccount = "_manifest";

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
    /// is where the configuration root is composed: the manifest section, root key vaults, the
    /// encryption key, the providers, and the store over them. This is the only place in the
    /// package that names concrete types across features.
    /// </summary>
    /// <remarks>
    /// The manifest section is the bootstrap: it gates which configuration directory is consulted
    /// at all, while root <c>keyVaults</c> entries define shared cloud key vaults across
    /// applications. An application that has never been set up has no section, and one is
    /// provisioned for it — naming the directory its options carry and preserving any existing
    /// root <c>keyVaults</c> — so referencing the package and registering it is the whole of what
    /// a developer must do. Only absence is filled in, so a manifest that exists but cannot be
    /// read, or is malformed, still fails. The application's local encryption key is likewise
    /// created when absent, whereas manifest vault secrets are unsealed strictly read-only from
    /// <c>nyssa-ai-configuration/_manifest</c>.
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
            IEncryption encryption = new LocalEncryption();

            // 1. The manifest section and root keyVaults: layered over what the developer said in
            //    code. An absent section is provisioned while preserving root keyVaults.
            var section = ConfigurationManifestReader.ReadApplication(manifestPath, applicationName);
            if (section.IsFailure && section.Errors[0].Category == ErrorCategory.NotFound)
                section = ProvisionSection(manifestPath, applicationName, options.ConfigDirectory, options.Bootstrap.LockTimeout, logger);

            if (section.IsFailure)
                throw Failed(section.Errors);

            var keyVaults = ResolveKeyVaults(options.Bootstrap, section.Value.KeyVaults, keyring, encryption);
            if (keyVaults.IsFailure)
                throw Failed(keyVaults.Errors);

            // 2. The configuration directory, protected to the current account if created here.
            var directory = section.Value.ConfigurationDirectory;
            var created = ProtectedFileSystem.CreateDirectory(directory);
            if (created.IsFailure)
                throw Failed(created.Errors);

            // 3. The application's local encryption key. Created once when absent.
            var key = GetOrCreateKey(applicationName, keyring, logger);
            if (key.IsFailure)
                throw Failed(key.Errors);

            var cipher = encryption.OpenCipher(key.Value);
            IRecovery recovery = new ConfigurationRecovery(key.Value, applicationName, directory);
            CryptographicOperations.ZeroMemory(key.Value);
            if (cipher.IsFailure)
                throw Failed(cipher.Errors);

            // 4. Providers: aesLocal and oauth always; plus every resolved key vault.
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
            foreach (var vaultEntry in keyVaults.Value)
            {
                var built = BuildKeyVault(vaultEntry, provider, storeReference, providers, credentials);
                if (built.IsFailure)
                    throw Failed(built.Errors);
            }

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

    /// <summary>Registers the built-in Bitwarden Secrets Manager (<c>bws</c>) cloud key vault factory.</summary>
    /// <param name="services">The container to register with.</param>
    /// <param name="factory">Optional pre-configured Bitwarden vault factory.</param>
    public static IServiceCollection AddBitwardenSecretsVault(
        this IServiceCollection services,
        CloudKeyVaultFactoryBitwarden? factory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICloudKeyVaultFactory>(
            factory ?? new CloudKeyVaultFactoryBitwarden()));
        return services;
    }

    /// <summary>
    /// Records or updates one root <c>keyVaults</c> entry in the configuration manifest, sealing
    /// any <see cref="VaultOptions.Secrets"/> with AES-256-GCM under the manifest encryption key at
    /// keyring service <see cref="KeyringService"/>, account <see cref="ManifestKeyAccount"/>.
    /// </summary>
    /// <param name="manifestPath">The manifest file path; created when absent.</param>
    /// <param name="vault">The vault definition to record.</param>
    /// <param name="keyring">Optional credential store for the manifest key; uses the platform default when null.</param>
    /// <param name="preserveExistingSecrets">
    /// When true, retains existing encrypted secret envelopes already on disk for keys not present
    /// in <see cref="VaultOptions.Secrets"/> without decrypting or re-encrypting them.
    /// </param>
    /// <param name="lockTimeout">Optional bounded wait for the manifest lock; defaults to 10 seconds.</param>
    public static void SetManifestKeyVault(
        string manifestPath,
        VaultOptions vault,
        IKeyring? keyring = null,
        bool preserveExistingSecrets = false,
        TimeSpan? lockTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);
        ArgumentNullException.ThrowIfNull(vault);

        if (string.IsNullOrWhiteSpace(manifestPath))
            throw Failed([ManifestPathBlank]);

        var validated = ValidateProgrammaticVault(vault);
        if (validated.IsFailure)
            throw Failed(validated.Errors);

        var entry = new VaultManifestEntry
        {
            Name = validated.Value.Name,
            Kind = validated.Value.Kind,
            Endpoint = validated.Value.Endpoint?.OriginalString,
            Credential = validated.Value.Credential,
            CacheTTLSeconds = validated.Value.CacheTTLSeconds,
            Settings = new Dictionary<string, string>(validated.Value.Settings, StringComparer.Ordinal),
        };

        Func<Result<Dictionary<string, ManifestSecretEnvelope>>>? sealSecrets = null;
        if (validated.Value.Secrets.Count > 0)
            sealSecrets = () => SealManifestSecrets(validated.Value.Name, validated.Value.Secrets, keyring);

        var written = ConfigurationManifestWriter.SetKeyVault(
            manifestPath,
            entry,
            sealSecrets,
            preserveExistingSecrets,
            lockTimeout);
        if (written.IsFailure)
            throw Failed(written.Errors);
    }

    /// <summary>
    /// Removes one named vault from the root <c>keyVaults</c> map in the configuration manifest.
    /// </summary>
    /// <param name="manifestPath">The manifest file path.</param>
    /// <param name="vaultName">The vault map key to remove.</param>
    /// <param name="lockTimeout">Optional bounded wait for the manifest lock; defaults to 10 seconds.</param>
    /// <returns><c>true</c> if the vault was present and removed; <c>false</c> when the manifest or vault did not exist.</returns>
    public static bool RemoveManifestKeyVault(
        string manifestPath,
        string vaultName,
        TimeSpan? lockTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);
        ArgumentNullException.ThrowIfNull(vaultName);

        if (string.IsNullOrWhiteSpace(manifestPath))
            throw Failed([ManifestPathBlank]);

        var removed = ConfigurationManifestWriter.RemoveKeyVault(manifestPath, vaultName, lockTimeout);
        if (removed.IsFailure)
            throw Failed(removed.Errors);

        return removed.Value;
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

    private static Result<byte[]> ReadManifestKey(IKeyring keyring)
    {
        var existing = KeyringManagedSecret.Get(KeyringService, ManifestKeyAccount, KeyLength, keyring);
        if (existing.IsFailure)
        {
            return existing.Errors[0].Category == ErrorCategory.NotFound
                ? Result<byte[]>.Failure(ManifestKeyMissing())
                : Result<byte[]>.Failure(ManifestKeyUnavailable(existing.Errors[0]));
        }

        return existing.Value is null
            ? Result<byte[]>.Failure(ManifestKeyMissing())
            : Result<byte[]>.Success(existing.Value);
    }

    private static Result<Dictionary<string, ManifestSecretEnvelope>> SealManifestSecrets(
        string vaultName,
        IDictionary<string, string> secrets,
        IKeyring? keyring)
    {
        var activeKeyring = keyring ?? KeyringDefault.Create();
        var key = KeyringManagedSecret.GetOrCreate(KeyringService, ManifestKeyAccount, KeyLength, activeKeyring);
        if (key.IsFailure)
            return Result<Dictionary<string, ManifestSecretEnvelope>>.Failure(ManifestKeyUnavailable(key.Errors[0]));

        var material = key.Value.Material;
        try
        {
            IEncryption encryption = new LocalEncryption();
            var cipher = encryption.OpenCipher(material);
            if (cipher.IsFailure)
                return Result<Dictionary<string, ManifestSecretEnvelope>>.Failure(cipher.Errors);

            var aadNamespace = $"keyVaults/{vaultName}";
            var sealedSecrets = new Dictionary<string, ManifestSecretEnvelope>(StringComparer.Ordinal);
            foreach (var (secretKey, secretPlaintext) in secrets)
            {
                var sealedValue = cipher.Value.Seal(secretPlaintext, aadNamespace, secretKey);
                if (sealedValue.IsFailure)
                    return Result<Dictionary<string, ManifestSecretEnvelope>>.Failure(sealedValue.Errors);

                sealedSecrets[secretKey] = new ManifestSecretEnvelope
                {
                    Value = sealedValue.Value,
                    Provider = "aesLocal",
                    Encrypted = true,
                };
            }

            return Result<Dictionary<string, ManifestSecretEnvelope>>.Success(sealedSecrets);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
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

    // Programmatic vaults provide defaults; root manifest keyVaults overlay them by vault name.
    // A manifest entry with the same name replaces the programmatic entry whole (inheriting
    // codeVault.CacheTTLSeconds only when manifest CacheTTLSeconds is omitted), while distinct
    // vault names coexist.
    private static Result<List<VaultOptions>> ResolveKeyVaults(
        BootstrapOptions bootstrap,
        IReadOnlyDictionary<string, VaultManifestEntry> manifestKeyVaults,
        IKeyring keyring,
        IEncryption encryption)
    {
        var orderedNames = new List<string>();
        var byName = new Dictionary<string, VaultOptions>(StringComparer.Ordinal);

        IEnumerable<VaultOptions?> programmatic = bootstrap.KeyVault is not null
            ? new VaultOptions?[] { bootstrap.KeyVault }.Concat(bootstrap.KeyVaults)
            : bootstrap.KeyVaults;

        foreach (var candidate in programmatic)
        {
            if (candidate is null)
                return Result<List<VaultOptions>>.Failure(ProgrammaticVaultNull);

            var validated = ValidateProgrammaticVault(candidate);
            if (validated.IsFailure)
                return Result<List<VaultOptions>>.Failure(validated.Errors);

            if (!byName.TryAdd(validated.Value.Name, validated.Value))
                return Result<List<VaultOptions>>.Failure(DuplicateProgrammaticVault(validated.Value.Name));

            orderedNames.Add(validated.Value.Name);
        }

        var hasManifestSecrets = manifestKeyVaults.Values.Any(vault => vault.Secrets is { Count: > 0 });
        IConfigurationCipher? manifestCipher = null;
        if (hasManifestSecrets)
        {
            var manifestKey = ReadManifestKey(keyring);
            if (manifestKey.IsFailure)
                return Result<List<VaultOptions>>.Failure(manifestKey.Errors);

            try
            {
                var opened = encryption.OpenCipher(manifestKey.Value);
                if (opened.IsFailure)
                    return Result<List<VaultOptions>>.Failure(opened.Errors);
                manifestCipher = opened.Value;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(manifestKey.Value);
            }
        }

        foreach (var (vaultName, fromManifest) in manifestKeyVaults)
        {
            byName.TryGetValue(vaultName, out var codeVault);
            var overlaid = BuildManifestVaultOptions(vaultName, fromManifest, codeVault, manifestCipher);
            if (overlaid.IsFailure)
                return Result<List<VaultOptions>>.Failure(overlaid.Errors);

            byName[vaultName] = overlaid.Value;
            if (codeVault is null)
                orderedNames.Add(vaultName);
        }

        var resolved = new List<VaultOptions>(orderedNames.Count);
        foreach (var name in orderedNames)
            resolved.Add(byName[name]);

        return Result<List<VaultOptions>>.Success(resolved);
    }

    private static Result<VaultOptions> ValidateProgrammaticVault(VaultOptions vault)
    {
        var nameValid = ConfigurationManifestReader.ValidateVaultName(vault.Name);
        if (nameValid.IsFailure)
            return Result<VaultOptions>.Failure(nameValid.Errors);

        var kindValid = ConfigurationManifestReader.ValidateVaultKind(vault.Name, vault.Kind);
        if (kindValid.IsFailure)
            return Result<VaultOptions>.Failure(kindValid.Errors);

        if (vault.Endpoint is not null)
        {
            if (!vault.Endpoint.IsAbsoluteUri
                || string.IsNullOrEmpty(vault.Endpoint.Authority)
                || vault.Endpoint.OriginalString.Any(char.IsControl))
            {
                return Result<VaultOptions>.Failure(VaultEndpointInvalid(vault.Name, vault.Endpoint.OriginalString));
            }
        }

        var credential = ConfigurationManifestReader.ValidateAndNormalizeCredential(vault.Name, vault.Credential);
        if (credential.IsFailure)
            return Result<VaultOptions>.Failure(credential.Errors);

        var settingsCopy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in vault.Settings)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) || value is null)
                return Result<VaultOptions>.Failure(VaultSettingInvalid(vault.Name));
            settingsCopy[key] = value;
        }

        var ttlValid = ConfigurationManifestReader.ValidateCacheTtl(vault.Name, vault.CacheTTLSeconds, settingsCopy);
        if (ttlValid.IsFailure)
            return Result<VaultOptions>.Failure(ttlValid.Errors);

        var secretsCopy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in vault.Secrets)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) || value is null)
                return Result<VaultOptions>.Failure(VaultSecretInvalid(vault.Name));
            secretsCopy[key] = value;
        }

        var cloned = new VaultOptions
        {
            Name = vault.Name,
            Kind = vault.Kind,
            Endpoint = vault.Endpoint,
            Credential = credential.Value,
            CacheTTLSeconds = vault.CacheTTLSeconds,
        };
        foreach (var (key, value) in settingsCopy)
            cloned.Settings[key] = value;
        foreach (var (key, value) in secretsCopy)
            cloned.Secrets[key] = value;

        return Result<VaultOptions>.Success(cloned);
    }

    private static Result<VaultOptions> BuildManifestVaultOptions(
        string vaultName,
        VaultManifestEntry fromManifest,
        VaultOptions? fromCode,
        IConfigurationCipher? manifestCipher)
    {
        Uri? endpoint = null;
        if (fromManifest.Endpoint is not null && !Uri.TryCreate(fromManifest.Endpoint, UriKind.Absolute, out endpoint))
            return Result<VaultOptions>.Failure(VaultEndpointInvalid(vaultName, fromManifest.Endpoint));

        if (fromManifest.Credential is not null && ConfigurationPointer.Parse(vaultName, fromManifest.Credential).IsFailure)
            return Result<VaultOptions>.Failure(VaultCredentialInvalid(vaultName, fromManifest.Credential));

        var vault = new VaultOptions
        {
            Name = vaultName,
            Kind = fromManifest.Kind!,
            Endpoint = endpoint,
            Credential = fromManifest.Credential,
            CacheTTLSeconds = fromManifest.CacheTTLSeconds ?? fromCode?.CacheTTLSeconds ?? VaultOptions.DefaultCacheTTLSeconds,
        };

        foreach (var (key, value) in fromManifest.Settings!)
            vault.Settings[key] = value;

        var aadNamespace = $"keyVaults/{vaultName}";
        foreach (var (secretKey, envelope) in fromManifest.Secrets!)
        {
            var unsealed = manifestCipher!.Open(envelope.Value, aadNamespace, secretKey);
            if (unsealed.IsFailure)
                return Result<VaultOptions>.Failure(ManifestSecretUnsealFailed(vaultName, secretKey, unsealed.Errors[0]));

            vault.Secrets[secretKey] = unsealed.Value;
        }

        return Result<VaultOptions>.Success(vault);
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
                return Result.Failure(VaultCredentialInvalid(name, entry.Credential));
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

    private static Error ManifestPathBlank { get; } = Error.FromMessages(
        "The configuration manifest path is not valid.",
        "manifest: manifestPath must not be blank",
        ErrorCategory.Validation);

    private static Error ProgrammaticVaultNull { get; } = Error.FromMessages(
        "A configured vault entry is null.",
        "bootstrap: BootstrapOptions.KeyVaults must not contain null entries",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the credential store could not supply or accept the key.</summary>
    private static Error KeyUnavailable(string applicationName, Error cause) => Error.FromMessages(
        "The application's encryption key is not available.",
        $"configuration: the encryption key for '{applicationName}' at keyring service '{KeyringService}', entry '{applicationName}' "
        + $"could not be obtained: {cause.TechnicalMessage}; the application needs a reachable credential store",
        cause.Category);

    private static Error ManifestKeyMissing() => Error.FromMessages(
        "The manifest encryption key is missing.",
        $"manifest: encryption key '{KeyringService}/{ManifestKeyAccount}' was not found in the credential store; "
        + "provision the manifest key before starting an application whose manifest carries encrypted vault secrets",
        ErrorCategory.Validation);

    private static Error ManifestKeyUnavailable(Error cause) => Error.FromMessages(
        "The manifest encryption key is not available.",
        $"manifest: encryption key '{KeyringService}/{ManifestKeyAccount}' could not be obtained: {cause.TechnicalMessage}",
        cause.Category);

    private static Error ManifestSecretUnsealFailed(string vaultName, string secretKey, Error cause) => Error.FromMessages(
        "A manifest vault secret could not be decrypted.",
        $"manifest: keyVaults.{vaultName}.secrets.{secretKey} could not be decrypted with '{KeyringService}/{ManifestKeyAccount}': {cause.TechnicalMessage}",
        ErrorCategory.Validation);

    private static Error DuplicateProgrammaticVault(string vaultName) => Error.FromMessages(
        "A vault name is configured more than once.",
        $"bootstrap: vault '{vaultName}' is configured more than once across BootstrapOptions.KeyVault and BootstrapOptions.KeyVaults",
        ErrorCategory.Validation);

    private static Error VaultSettingInvalid(string vaultName) => Error.FromMessages(
        "A configured vault setting is not valid.",
        $"bootstrap: vault '{vaultName}' contains a blank, control-character, or null setting entry",
        ErrorCategory.Validation);

    private static Error VaultSecretInvalid(string vaultName) => Error.FromMessages(
        "A configured vault secret is not valid.",
        $"bootstrap: vault '{vaultName}' contains a blank, control-character, or null secret entry",
        ErrorCategory.Validation);

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
        public T Add<T>(T item) where T : IDisposable { _items.Add(item); return item; }
        public void Dispose() { foreach (var item in _items) item.Dispose(); }
    }
}
