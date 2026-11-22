using System.Security.Cryptography;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Encryption;
using NyssaAI.Configuration.Manifest;
using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// Layers root manifest <c>keyVaults</c> over programmatic <see cref="BootstrapOptions"/> vaults
/// and seals or unseals vault bootstrap secrets under the shared <c>_manifest</c> keyring key.
/// </summary>
internal static class ManifestVaultResolver
{
    private const int KeyLength = 32;

    // Programmatic vaults provide defaults; root manifest keyVaults overlay them by vault name.
    // A manifest entry with the same name replaces the programmatic entry whole (inheriting
    // codeVault.CacheTTLSeconds only when manifest CacheTTLSeconds is omitted), while distinct
    // vault names coexist.
    internal static Result<List<VaultOptions>> ResolveKeyVaults(
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

    internal static Result<VaultOptions> ValidateProgrammaticVault(VaultOptions vault)
    {
        var nameValid = ManifestVaultValidator.ValidateVaultName(vault.Name);
        if (nameValid.IsFailure)
            return Result<VaultOptions>.Failure(nameValid.Errors);

        var kindValid = ManifestVaultValidator.ValidateVaultKind(vault.Name, vault.Kind);
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

        var credential = ManifestVaultValidator.ValidateAndNormalizeCredential(vault.Name, vault.Credential);
        if (credential.IsFailure)
            return Result<VaultOptions>.Failure(credential.Errors);

        var settingsCopy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in vault.Settings)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) || value is null)
                return Result<VaultOptions>.Failure(VaultSettingInvalid(vault.Name));
            settingsCopy[key] = value;
        }

        var ttlValid = ManifestVaultValidator.ValidateCacheTtl(vault.Name, vault.CacheTTLSeconds, settingsCopy);
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

    internal static Result<Dictionary<string, ManifestSecretEnvelope>> SealManifestSecrets(
        string vaultName,
        IDictionary<string, string> secrets,
        IKeyring? keyring)
    {
        var activeKeyring = keyring ?? KeyringDefault.Create();
        var key = KeyringManagedSecret.GetOrCreate(
            ConfigurationServiceCollectionExtensions.KeyringService,
            ConfigurationServiceCollectionExtensions.ManifestKeyAccount,
            KeyLength,
            activeKeyring);
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

    /// <summary>The failure returned when a vault's credential is not a <c>namespace/key</c> address.</summary>
    internal static Error VaultCredentialInvalid(string vault, string credential) => Error.FromMessages(
        "The configuration manifest is malformed.",
        $"manifest: vault '{vault}' names credential '{credential}', which is not a namespace/key address",
        ErrorCategory.Validation);

    private static Result<byte[]> ReadManifestKey(IKeyring keyring)
    {
        var existing = KeyringManagedSecret.Get(
            ConfigurationServiceCollectionExtensions.KeyringService,
            ConfigurationServiceCollectionExtensions.ManifestKeyAccount,
            KeyLength,
            keyring);
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

    private static Error ProgrammaticVaultNull { get; } = Error.FromMessages(
        "A configured vault entry is null.",
        "bootstrap: BootstrapOptions.KeyVaults must not contain null entries",
        ErrorCategory.Validation);

    private static Error ManifestKeyMissing() => Error.FromMessages(
        "The manifest encryption key is missing.",
        $"manifest: encryption key '{ConfigurationServiceCollectionExtensions.KeyringService}/{ConfigurationServiceCollectionExtensions.ManifestKeyAccount}' was not found in the credential store; "
        + "provision the manifest key before starting an application whose manifest carries encrypted vault secrets",
        ErrorCategory.Validation);

    private static Error ManifestKeyUnavailable(Error cause) => Error.FromMessages(
        "The manifest encryption key is not available.",
        $"manifest: encryption key '{ConfigurationServiceCollectionExtensions.KeyringService}/{ConfigurationServiceCollectionExtensions.ManifestKeyAccount}' could not be obtained: {cause.TechnicalMessage}",
        cause.Category);

    private static Error ManifestSecretUnsealFailed(string vaultName, string secretKey, Error cause) => Error.FromMessages(
        "A manifest vault secret could not be decrypted.",
        $"manifest: keyVaults.{vaultName}.secrets.{secretKey} could not be decrypted with '{ConfigurationServiceCollectionExtensions.KeyringService}/{ConfigurationServiceCollectionExtensions.ManifestKeyAccount}': {cause.TechnicalMessage}",
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

    private static Error VaultEndpointInvalid(string vault, string endpoint) => Error.FromMessages(
        "The configuration manifest is malformed.",
        $"manifest: vault '{vault}' names endpoint '{endpoint}', which is not an absolute URI",
        ErrorCategory.Validation);
}
