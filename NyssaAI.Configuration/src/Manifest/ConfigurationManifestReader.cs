using System.Globalization;
using System.Text.Json;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// ConfigurationManifestReader finds and reads the manifest, and settles one application's
/// section into the shape the composer needs.
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
/// accounts. When the section's key vault carries secrets it must not be readable by them
/// either, and a manifest that is fails startup naming the bit and the command that fixes it.
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
        try
        {
            using var stream = File.OpenRead(path);
            var manifest = JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.ConfigurationManifest);
            if (manifest is null || manifest.Version != ConfigurationManifest.SupportedVersion)
                return Result<ConfigurationManifest?>.Failure(Unsupported(path));

            return Result<ConfigurationManifest?>.Success(Normalize(manifest));
        }
        catch (JsonException exception)
        {
            return Result<ConfigurationManifest?>.Failure(Malformed(path, exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<ConfigurationManifest?>.Failure(Unreadable(path, exception));
        }
    }

    /// <summary>Reads one application's section and settles it: directory absolute, timeout parsed, secrets guarded.</summary>
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

        var lockTimeout = ParseLockTimeout(section.LockTimeout, path, applicationName);
        if (lockTimeout.IsFailure)
            return Result<ApplicationManifestSettings>.Failure(lockTimeout.Errors);

        if (section.KeyVault?.Secrets!.Count > 0)
        {
            var privateFile = DirectorySafety.VerifyPrivateFile(path);
            if (privateFile.IsFailure)
                return Result<ApplicationManifestSettings>.Failure(privateFile.Errors);
        }

        return Result<ApplicationManifestSettings>.Success(new ApplicationManifestSettings
        {
            ConfigurationDirectory = ResolveDirectory(path, section.ConfigurationDirectory),
            LockTimeout = lockTimeout.Value,
            KeyVault = section.KeyVault,
        });
    }

    /// <summary>A relative directory is relative to the manifest's own folder, so a section can say <c>"SomeApp-cli"</c>.</summary>
    public static string ResolveDirectory(string manifestPath, string configurationDirectory) =>
        Path.GetFullPath(Path.IsPathRooted(configurationDirectory)
            ? configurationDirectory
            : Path.Combine(Path.GetDirectoryName(manifestPath)!, configurationDirectory));

    // An absent key deserializes as null rather than as the property's initializer, so the shape
    // the composer sees is settled here: no applications is an empty map, and a key vault with no
    // settings or secrets has empty maps. A section with no key vault keeps its null, which is the
    // application that uses none. Nothing downstream has to ask.
    private static ConfigurationManifest Normalize(ConfigurationManifest manifest)
    {
        var applications = new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal);
        foreach (var (name, section) in manifest.Applications ?? [])
        {
            applications[name] = section.KeyVault is null ? section : section with
            {
                KeyVault = section.KeyVault with
                {
                    Settings = section.KeyVault.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    Secrets = section.KeyVault.Secrets ?? new Dictionary<string, string>(StringComparer.Ordinal),
                },
            };
        }

        return manifest with { Applications = applications };
    }

    private static Result<TimeSpan?> ParseLockTimeout(string? value, string path, string applicationName)
    {
        if (value is null)
            return Result<TimeSpan?>.Success(null);

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeout) && timeout > TimeSpan.Zero
            ? Result<TimeSpan?>.Success(timeout)
            : Result<TimeSpan?>.Failure(Error.FromMessages(
                "The configuration manifest is malformed.",
                $"manifest: '{path}' names lockTimeout '{value}' for '{applicationName}', which is not a positive TimeSpan",
                ErrorCategory.Validation));
    }

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

/// <summary>One application's section, settled: what the composer opens the store from.</summary>
internal sealed record ApplicationManifestSettings
{
    /// <summary>Gets the absolute configuration directory.</summary>
    public required string ConfigurationDirectory { get; init; }

    /// <summary>Gets the document lock timeout the section names, or null for the default.</summary>
    public TimeSpan? LockTimeout { get; init; }

    /// <summary>Gets the key vault the section names, normalized, or null when it names none.</summary>
    public VaultManifestEntry? KeyVault { get; init; }
}
