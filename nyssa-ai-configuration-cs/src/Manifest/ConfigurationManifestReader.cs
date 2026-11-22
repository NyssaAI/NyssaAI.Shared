using System.Globalization;
using System.Text.Json;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// ConfigurationManifestReader finds and reads the manifest, validates root <c>keyVaults</c> and
/// encrypted secret envelopes, and settles one application's section into the shape the composer needs.
/// </summary>
/// <remarks>
/// <para>
/// Absence and unreadability are different outcomes. An absent manifest, an absent section, or a
/// section with no directory means the configuration could not be found, and startup says so by
/// name rather than guessing a location. A file that exists but cannot be read is a failure of
/// its own: guessing "not configured" from a permissions error would be exactly the wrong guess.
/// </para>
/// <para>
/// The manifest decides which directory is consulted at all, so it must not be writable by other
/// accounts. When any vault carries secrets it must not be readable by them either, and a manifest
/// that is fails startup naming the bit and the command that fixes it — before any legacy-section
/// or envelope validation runs.
/// </para>
/// </remarks>
internal static class ConfigurationManifestReader
{
    /// <summary>The manifest's file name under the user's configuration root.</summary>
    public const string FileName = "config-manifest.json";

    /// <summary>The default manifest path for one home directory: <c>&lt;home&gt;/.nyssa-ai/config-manifest.json</c>.</summary>
    public static string DefaultPathFor(string homeDirectory) =>
        Path.Combine(homeDirectory, ".nyssa-ai", FileName);

    /// <summary>Reads the whole manifest, yielding null when there is none.</summary>
    /// <param name="path">The manifest path.</param>
    public static Result<ConfigurationManifest?> Read(string path)
    {
        if (!File.Exists(path))
            return Result<ConfigurationManifest?>.Success(null);

        var fileSafe = DirectorySafety.VerifyFile(path);
        if (fileSafe.IsFailure)
            return Result<ConfigurationManifest?>.Failure(fileSafe.Errors);

        // This is the adapter boundary: the file system and System.Text.Json throw here and
        // nowhere above it.
        ConfigurationManifest? manifest;
        try
        {
            using var stream = File.OpenRead(path);
            manifest = JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.ConfigurationManifest);
        }
        catch (JsonException exception)
        {
            return Result<ConfigurationManifest?>.Failure(Malformed(path, exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<ConfigurationManifest?>.Failure(Unreadable(path, exception));
        }

        if (manifest is null || manifest.Version != ConfigurationManifest.SupportedVersion)
            return Result<ConfigurationManifest?>.Failure(Unsupported(path));

        // P0-3: Run the 0600 private-file gate FIRST whenever any root or legacy vault carries
        // non-empty secrets (even if plaintext or malformed), before legacy or envelope checks.
        if (HasAnySecrets(manifest))
        {
            var privateFile = DirectorySafety.VerifyPrivateFile(path);
            if (privateFile.IsFailure)
                return Result<ConfigurationManifest?>.Failure(privateFile.Errors);
        }

        var registryResult = ManifestNamespaceRegistry.Parse(path, manifest.NamespaceRegistry);
        if (registryResult.IsFailure)
            return Result<ConfigurationManifest?>.Failure(registryResult.Errors);
        var namespaceRegistry = registryResult.Value;

        var applications = new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal);
        foreach (var (appName, section) in manifest.Applications ?? [])
        {
            if (section is null)
            {
                return Result<ConfigurationManifest?>.Failure(ValidationFailure(
                    $"manifest: applications.{appName} in '{path}' must be an object"));
            }

            if (section.KeyVault is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
                return Result<ConfigurationManifest?>.Failure(LegacyApplicationKeyVault(appName));

            var sharedValid = ManifestNamespaceRegistry.ValidateSharedNamespaces(appName, section.SharedNamespaces, namespaceRegistry);
            if (sharedValid.IsFailure)
                return Result<ConfigurationManifest?>.Failure(sharedValid.Errors);

            applications[appName] = section with { KeyVault = null };
        }

        var keyVaults = new Dictionary<string, VaultManifestEntry>(StringComparer.Ordinal);
        foreach (var (vaultName, entry) in manifest.KeyVaults ?? [])
        {
            if (entry is null)
            {
                return Result<ConfigurationManifest?>.Failure(ValidationFailure(
                    $"manifest: keyVaults.{vaultName} in '{path}' must be an object"));
            }

            var validated = ManifestVaultValidator.ValidateAndNormalizeVault(path, vaultName, entry);
            if (validated.IsFailure)
                return Result<ConfigurationManifest?>.Failure(validated.Errors);

            keyVaults[vaultName] = validated.Value;
        }

        return Result<ConfigurationManifest?>.Success(manifest with
        {
            KeyVaults = keyVaults,
            NamespaceRegistry = namespaceRegistry,
            Applications = applications,
        });
    }

    /// <summary>Reads one application's section and settles it: directory absolute, timeout parsed, vaults validated.</summary>
    /// <param name="path">The manifest path.</param>
    /// <param name="applicationName">The application whose section is wanted.</param>
    public static Result<ApplicationManifestSettings> ReadApplication(string path, string applicationName)
    {
        var manifest = Read(path);
        if (manifest.IsFailure)
            return Result<ApplicationManifestSettings>.Failure(manifest.Errors);

        if (manifest.Value is null)
            return Result<ApplicationManifestSettings>.Failure(ManifestNotFound(path, applicationName));

        if (!manifest.Value.Applications!.TryGetValue(applicationName, out var section))
            return Result<ApplicationManifestSettings>.Failure(ApplicationNotListed(path, applicationName));

        if (string.IsNullOrWhiteSpace(section.ConfigurationDirectory))
            return Result<ApplicationManifestSettings>.Failure(DirectoryNotNamed(path, applicationName));

        var lockTimeout = ParseLockTimeout(section, path, applicationName);
        if (lockTimeout.IsFailure)
            return Result<ApplicationManifestSettings>.Failure(lockTimeout.Errors);

        var namespaceFiles = ManifestNamespaceRegistry.ResolveApplicationFiles(
            path, section.SharedNamespaces, manifest.Value.NamespaceRegistry!);

        return Result<ApplicationManifestSettings>.Success(new ApplicationManifestSettings
        {
            ConfigurationDirectory = ResolveDirectory(path, section.ConfigurationDirectory),
            LockTimeout = lockTimeout.Value,
            KeyVaults = manifest.Value.KeyVaults!,
            NamespaceFiles = namespaceFiles,
        });
    }

    /// <summary>A relative directory is relative to the manifest's own folder, so a section can say <c>"SomeApp-cli"</c>.</summary>
    public static string ResolveDirectory(string manifestPath, string configurationDirectory) =>
        Path.GetFullPath(Path.IsPathRooted(configurationDirectory)
            ? configurationDirectory
            : Path.Combine(Path.GetDirectoryName(manifestPath)!, configurationDirectory));

    private static bool HasAnySecrets(ConfigurationManifest manifest)
    {
        if (manifest.KeyVaults is not null)
        {
            foreach (var entry in manifest.KeyVaults.Values)
            {
                if (entry is not null && HasSecretsElement(entry.RawSecrets))
                    return true;
            }
        }

        if (manifest.Applications is not null)
        {
            foreach (var section in manifest.Applications.Values)
            {
                if (section?.KeyVault is { ValueKind: JsonValueKind.Object } legacyVault
                    && legacyVault.TryGetProperty("secrets", out var legacySecrets)
                    && HasSecretsElement(legacySecrets))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSecretsElement(JsonElement? rawSecrets)
    {
        if (rawSecrets is not { } element)
            return false;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

        return element.ValueKind != JsonValueKind.Object || element.EnumerateObject().MoveNext();
    }

    private static Result<TimeSpan?> ParseLockTimeout(ApplicationManifestEntry section, string path, string applicationName)
    {
        if (section.LockTimeoutMs.HasValue)
        {
            if (section.LockTimeoutMs.Value <= 0)
            {
                return Result<TimeSpan?>.Failure(Error.FromMessages(
                    "The configuration manifest is malformed.",
                    $"manifest: '{path}' names lockTimeoutMs '{section.LockTimeoutMs.Value}' for '{applicationName}', which is not a positive integer",
                    ErrorCategory.Validation));
            }
            return Result<TimeSpan?>.Success(TimeSpan.FromMilliseconds(section.LockTimeoutMs.Value));
        }

        if (section.LockTimeout is null)
            return Result<TimeSpan?>.Success(null);

        if (long.TryParse(section.LockTimeout, CultureInfo.InvariantCulture, out var ms) && ms > 0)
            return Result<TimeSpan?>.Success(TimeSpan.FromMilliseconds(ms));

        return TimeSpan.TryParse(section.LockTimeout, CultureInfo.InvariantCulture, out var timeout) && timeout > TimeSpan.Zero
            ? Result<TimeSpan?>.Success(timeout)
            : Result<TimeSpan?>.Failure(Error.FromMessages(
                "The configuration manifest is malformed.",
                $"manifest: '{path}' names lockTimeout '{section.LockTimeout}' for '{applicationName}', which is not a positive TimeSpan",
                ErrorCategory.Validation));
    }

    internal static Error ValidationFailure(string technicalMessage) =>
        ManifestVaultValidator.ValidationFailure(technicalMessage);

    /// <summary>The failure returned when a legacy per-application <c>keyVault</c> property is present.</summary>
    public static Error LegacyApplicationKeyVault(string applicationName) => Error.FromMessages(
        "The configuration manifest uses a legacy per-application keyVault section.",
        $"manifest: applications.{ManifestVaultValidator.SanitizeForError(applicationName)}.keyVault is no longer supported; move vault definitions to root 'keyVaults'",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a vault secret is plaintext or not a valid encrypted <c>aesLocal</c> envelope.</summary>
    public static Error SecretEnvelopeInvalid(string vaultName, string secretKey) =>
        ManifestVaultValidator.SecretEnvelopeInvalid(vaultName, secretKey);

    /// <summary>The failure returned when there is no manifest at all.</summary>
    public static Error ManifestNotFound(string path, string applicationName) => Error.FromMessages(
        "The configuration could not be found.",
        $"manifest: no manifest at '{path}'; run the application's init command, or create the file with a section "
        + $"for '{applicationName}' naming its configurationDirectory",
        ErrorCategory.NotFound);

    /// <summary>The failure returned when the manifest exists but has no section for the application.</summary>
    public static Error ApplicationNotListed(string path, string applicationName) => Error.FromMessages(
        "The configuration could not be found.",
        $"manifest: '{path}' has no section for '{applicationName}'; run the application's init command, or add "
        + $"applications.{applicationName}.configurationDirectory",
        ErrorCategory.NotFound);

    /// <summary>The failure returned when the application's section names no directory.</summary>
    public static Error DirectoryNotNamed(string path, string applicationName) => Error.FromMessages(
        "The configuration could not be found.",
        $"manifest: the section for '{applicationName}' in '{path}' names no configurationDirectory",
        ErrorCategory.NotFound);

    /// <summary>The failure returned when the manifest exists but cannot be read.</summary>
    public static Error Unreadable(string path, Exception exception) => Error.FromMessages(
        "The configuration manifest could not be read.",
        $"manifest: reading '{path}' failed: {exception.GetType().Name}: {exception.Message}",
        exception is UnauthorizedAccessException ? ErrorCategory.Forbidden : ErrorCategory.Unexpected);

    /// <summary>The failure returned when the manifest is not valid JSON of the expected shape.</summary>
    public static Error Malformed(string path, JsonException exception) => Error.FromMessages(
        "The configuration manifest is malformed.",
        $"manifest: '{path}' failed validation: {exception.Message}",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the manifest does not carry the supported schema version.</summary>
    public static Error Unsupported(string path) => Error.FromMessages(
        "The configuration manifest has an unsupported shape.",
        $"manifest: '{path}' is not a version {ConfigurationManifest.SupportedVersion} manifest",
        ErrorCategory.Validation);
}
