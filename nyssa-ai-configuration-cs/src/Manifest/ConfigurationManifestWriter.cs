using System.Diagnostics;
using System.Text.Json;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// ConfigurationManifestWriter records application sections and shared root <c>keyVaults</c>
/// entries in the manifest under exclusive sidecar locking and atomic temporary-file publication.
/// </summary>
/// <remarks>
/// The write is exclusive and atomic: a lock file beside the manifest bounds the wait, and the
/// new content lands through a temporary file renamed into place, so a reader never sees a torn
/// write and two processes updating the manifest at once do not lose each other's changes.
/// </remarks>
internal static class ConfigurationManifestWriter
{
    /// <summary>The default maximum wait for exclusive manifest access.</summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Sets the application's configuration directory, for the callers that are themselves
    /// synchronous — the composer provisioning the section a machine never had.
    /// </summary>
    /// <param name="path">The manifest path; created when absent.</param>
    /// <param name="applicationName">The application whose section is written.</param>
    /// <param name="configurationDirectory">The directory to record.</param>
    /// <param name="lockTimeout">The bounded wait for the manifest lock; <see cref="DefaultLockTimeout"/> when null.</param>
    /// <returns>The directory now on record.</returns>
    public static Result<string> SetConfigurationDirectory(
        string path,
        string applicationName,
        string configurationDirectory,
        TimeSpan? lockTimeout = null)
    {
        var locked = AcquireLock(path, lockTimeout ?? DefaultLockTimeout);
        if (locked.IsFailure)
            return Result<string>.Failure(locked.Errors);

        using var lockStream = locked.Value;

        var recorded = Record(path, applicationName, configurationDirectory, lockTimeout);
        if (recorded.IsFailure)
            return Result<string>.Failure(recorded.Errors);

        var published = Publish(path, recorded.Value);
        return published.IsFailure
            ? Result<string>.Failure(published.Errors)
            : Result<string>.Success(configurationDirectory);
    }

    /// <summary>
    /// Records or updates one root <c>keyVaults</c> entry under the manifest lock, preserving
    /// sibling vaults and application sections.
    /// </summary>
    /// <param name="path">The manifest path; created when absent.</param>
    /// <param name="vault">The normalized vault entry (with pre-sealed <see cref="VaultManifestEntry.Secrets"/> when present).</param>
    /// <param name="preserveExistingSecrets">
    /// When true, retains existing encrypted secret envelopes already on disk for keys not present
    /// in <paramref name="vault"/>.
    /// </param>
    /// <param name="lockTimeout">The bounded wait for the manifest lock; <see cref="DefaultLockTimeout"/> when null.</param>
    public static Result SetKeyVault(
        string path,
        VaultManifestEntry vault,
        bool preserveExistingSecrets = false,
        TimeSpan? lockTimeout = null) =>
        SetKeyVault(path, vault, sealSecrets: null, preserveExistingSecrets, lockTimeout);

    /// <summary>
    /// Records or updates one root <c>keyVaults</c> entry under the manifest lock, invoking
    /// <paramref name="sealSecrets"/> only after the existing manifest has been read and validated.
    /// </summary>
    internal static Result SetKeyVault(
        string path,
        VaultManifestEntry vault,
        Func<Result<Dictionary<string, ManifestSecretEnvelope>>>? sealSecrets,
        bool preserveExistingSecrets = false,
        TimeSpan? lockTimeout = null)
    {
        var nameValid = ConfigurationManifestReader.ValidateVaultName(vault.Name);
        if (nameValid.IsFailure)
            return nameValid;

        var vaultName = vault.Name!;
        var kindValid = ConfigurationManifestReader.ValidateVaultKind(vaultName, vault.Kind);
        if (kindValid.IsFailure)
            return kindValid;

        var endpointValid = ConfigurationManifestReader.ValidateEndpointString(vaultName, vault.Endpoint);
        if (endpointValid.IsFailure)
            return endpointValid;

        var normalizedCredential = ConfigurationManifestReader.ValidateAndNormalizeCredential(vaultName, vault.Credential);
        if (normalizedCredential.IsFailure)
            return Result.Failure(normalizedCredential.Errors);

        var ttlValid = ConfigurationManifestReader.ValidateCacheTtl(vaultName, vault.CacheTTLSeconds, vault.Settings, path);
        if (ttlValid.IsFailure)
            return ttlValid;

        if (sealSecrets is null && vault.Secrets is not null)
        {
            var secretsValid = ConfigurationManifestReader.ValidateNormalizedSecrets(vaultName, vault.Secrets);
            if (secretsValid.IsFailure)
                return secretsValid;
        }

        var locked = AcquireLock(path, lockTimeout ?? DefaultLockTimeout);
        if (locked.IsFailure)
            return Result.Failure(locked.Errors);

        using var lockStream = locked.Value;

        var existing = ConfigurationManifestReader.Read(path);
        if (existing.IsFailure)
            return Result.Failure(existing.Errors);

        var manifest = existing.Value ?? new ConfigurationManifest
        {
            KeyVaults = new Dictionary<string, VaultManifestEntry>(StringComparer.Ordinal),
            Applications = new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal),
        };

        var mergedSecrets = new Dictionary<string, ManifestSecretEnvelope>(StringComparer.Ordinal);
        if (preserveExistingSecrets
            && manifest.KeyVaults is not null
            && manifest.KeyVaults.TryGetValue(vaultName, out var previous)
            && previous.Secrets is not null)
        {
            foreach (var (key, envelope) in previous.Secrets)
                mergedSecrets[key] = envelope;
        }

        if (sealSecrets is not null)
        {
            var sealedResult = sealSecrets();
            if (sealedResult.IsFailure)
                return Result.Failure(sealedResult.Errors);

            foreach (var (key, envelope) in sealedResult.Value)
                mergedSecrets[key] = envelope;
        }
        else if (vault.Secrets is not null)
        {
            foreach (var (key, envelope) in vault.Secrets)
                mergedSecrets[key] = envelope;
        }

        var keyVaults = new Dictionary<string, VaultManifestEntry>(
            manifest.KeyVaults ?? [],
            StringComparer.Ordinal)
        {
            [vaultName] = vault with
            {
                Name = vaultName,
                Credential = normalizedCredential.Value,
                Settings = vault.Settings is not null
                    ? new Dictionary<string, string>(vault.Settings, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal),
                Secrets = mergedSecrets,
                RawSecrets = null,
            },
        };

        return Publish(path, manifest with
        {
            KeyVaults = keyVaults,
            Applications = manifest.Applications ?? new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal),
        });
    }

    /// <summary>
    /// Removes one root <c>keyVaults</c> entry under the manifest lock if present.
    /// </summary>
    /// <param name="path">The manifest path.</param>
    /// <param name="vaultName">The vault map key to remove.</param>
    /// <param name="lockTimeout">The bounded wait for the manifest lock; <see cref="DefaultLockTimeout"/> when null.</param>
    /// <returns><c>true</c> when the vault existed and was removed; <c>false</c> when the manifest or vault was absent.</returns>
    public static Result<bool> RemoveKeyVault(
        string path,
        string vaultName,
        TimeSpan? lockTimeout = null)
    {
        var nameValid = ConfigurationManifestReader.ValidateVaultName(vaultName);
        if (nameValid.IsFailure)
            return Result<bool>.Failure(nameValid.Errors);

        if (!File.Exists(path))
            return Result<bool>.Success(false);

        var locked = AcquireLock(path, lockTimeout ?? DefaultLockTimeout);
        if (locked.IsFailure)
            return Result<bool>.Failure(locked.Errors);

        using var lockStream = locked.Value;

        var existing = ConfigurationManifestReader.Read(path);
        if (existing.IsFailure)
            return Result<bool>.Failure(existing.Errors);

        if (existing.Value is null || existing.Value.KeyVaults is null)
            return Result<bool>.Success(false);

        var keyVaults = new Dictionary<string, VaultManifestEntry>(existing.Value.KeyVaults, StringComparer.Ordinal);
        if (!keyVaults.Remove(vaultName))
            return Result<bool>.Success(false);

        var published = Publish(path, existing.Value with { KeyVaults = keyVaults });
        return published.IsFailure
            ? Result<bool>.Failure(published.Errors)
            : Result<bool>.Success(true);
    }

    /// <summary>
    /// Compacts empty maps and omits redundant fields before serialization so a minimal manifest
    /// writes as <c>{"version": 1, "applications": {...}}</c>.
    /// </summary>
    internal static ConfigurationManifest PrepareForWrite(ConfigurationManifest manifest)
    {
        Dictionary<string, VaultManifestEntry>? keyVaults = null;
        if (manifest.KeyVaults is { Count: > 0 })
        {
            keyVaults = new Dictionary<string, VaultManifestEntry>(StringComparer.Ordinal);
            foreach (var (vaultName, entry) in manifest.KeyVaults)
            {
                JsonElement? rawSecrets = entry.Secrets is { Count: > 0 }
                    ? JsonSerializer.SerializeToElement(
                        entry.Secrets,
                        ManifestJsonContext.Default.DictionaryStringManifestSecretEnvelope)
                    : null;

                keyVaults[vaultName] = entry with
                {
                    Name = null,
                    Settings = entry.Settings is { Count: > 0 }
                        ? new Dictionary<string, string>(entry.Settings, StringComparer.Ordinal)
                        : null,
                    RawSecrets = rawSecrets,
                    Secrets = null,
                };
            }
        }

        var applications = new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal);
        foreach (var (appName, section) in manifest.Applications ?? [])
            applications[appName] = section with { KeyVault = null };

        return manifest with
        {
            KeyVaults = keyVaults,
            Applications = applications,
        };
    }

    // Reads the manifest and returns it with this application's directory set, leaving every other
    // section, root keyVaults, and everything else in this section exactly as it was.
    private static Result<ConfigurationManifest> Record(
        string path,
        string applicationName,
        string configurationDirectory,
        TimeSpan? lockTimeout)
    {
        var existing = ConfigurationManifestReader.Read(path);
        if (existing.IsFailure)
            return Result<ConfigurationManifest>.Failure(existing.Errors);

        var manifest = existing.Value ?? new ConfigurationManifest
        {
            KeyVaults = new Dictionary<string, VaultManifestEntry>(StringComparer.Ordinal),
            Applications = new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal),
        };
        var applications = new Dictionary<string, ApplicationManifestEntry>(manifest.Applications!, StringComparer.Ordinal);
        var section = applications.GetValueOrDefault(applicationName) ?? new ApplicationManifestEntry();
        var updated = section with { ConfigurationDirectory = configurationDirectory, KeyVault = null };
        if (section.LockTimeout is null && section.LockTimeoutMs is null)
        {
            updated = updated with
            {
                LockTimeoutMs = (int)Math.Round((lockTimeout ?? DefaultLockTimeout).TotalMilliseconds),
            };
        }
        else if (lockTimeout.HasValue && section.LockTimeout is null)
        {
            updated = updated with
            {
                LockTimeoutMs = (int)Math.Round(lockTimeout.Value.TotalMilliseconds),
            };
        }

        applications[applicationName] = updated;

        return Result<ConfigurationManifest>.Success(manifest with { Applications = applications });
    }

    /// <summary>The failure returned when the manifest cannot be written.</summary>
    public static Error NotWritable(string path, Exception exception) => Error.FromMessages(
        "The configuration manifest could not be saved.",
        $"manifest: writing '{path}' failed: {exception.GetType().Name}: {exception.Message}",
        exception is UnauthorizedAccessException ? ErrorCategory.Forbidden : ErrorCategory.Unexpected);

    /// <summary>The failure returned when the manifest lock cannot be acquired within its bound.</summary>
    public static Error LockNotAcquired(string path, TimeSpan waited) => Error.FromMessages(
        "The configuration manifest is in use. Try again.",
        $"manifest: could not take the lock on '{path}' within {waited.TotalSeconds:0.###}s",
        ErrorCategory.Unavailable);

    // The synchronous twin of PublishAsync: same temporary file, same rename, same cleanup.
    private static Result Publish(string path, ConfigurationManifest manifest)
    {
        var created = ProtectedFileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        if (created.IsFailure)
            return created;

        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var prepared = PrepareForWrite(manifest);
            var content = JsonSerializer.Serialize(prepared, ManifestJsonContext.Default.ConfigurationManifest);
            var written = ProtectedFileSystem.WriteAllText(temporary, content);
            if (written.IsFailure)
                return written;

            // This is the adapter boundary: the file system throws here and nowhere above it.
            try
            {
                File.Move(temporary, path, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Result.Failure(NotWritable(path, exception));
            }

            return Result.Success();
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Cleanup cannot replace the publication result.
            }
        }
    }

    // The synchronous twin of AcquireLockAsync: the same lock file and the same bounded wait,
    // waiting on the calling thread because the caller has nothing else to do with it.
    private static Result<FileStream> AcquireLock(string path, TimeSpan lockTimeout)
    {
        var lockPath = path + ".lock";
        var created = ProtectedFileSystem.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        if (created.IsFailure)
            return Result<FileStream>.Failure(created.Errors);

        var safe = DirectorySafety.Verify(Path.GetDirectoryName(lockPath)!);
        if (safe.IsFailure)
            return Result<FileStream>.Failure(safe.Errors);

        // This is the adapter boundary: the lock file throws for a read-only or directory-shaped path.
        try
        {
            var started = Stopwatch.GetTimestamp();
            var backoff = TimeSpan.FromMilliseconds(5);
            while (true)
            {
                try
                {
                    return Result<FileStream>.Success(new FileStream(
                        lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (IOException)
                {
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    if (elapsed >= lockTimeout)
                        return Result<FileStream>.Failure(LockNotAcquired(path, elapsed));

                    var remaining = lockTimeout - elapsed;
                    Thread.Sleep(backoff < remaining ? backoff : remaining);
                    backoff = backoff < TimeSpan.FromMilliseconds(50) ? backoff * 2 : backoff;
                }
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result<FileStream>.Failure(NotWritable(path, exception));
        }
    }
}
