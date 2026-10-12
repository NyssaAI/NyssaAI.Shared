using System.Diagnostics;
using System.Text.Json;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// ConfigurationManifestWriter records one application's configuration directory in the manifest.
/// It is the one write the library makes to the manifest — the default section provisioned for an
/// application that has never been set up; everything else in a section is an operator's to write.
/// </summary>
/// <remarks>
/// The write is exclusive and atomic: a lock file beside the manifest bounds the wait, and the
/// new content lands through a temporary file renamed into place, so a reader never sees a torn
/// write and two applications initialising at once do not lose each other's sections.
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

        var recorded = Record(path, applicationName, configurationDirectory);
        if (recorded.IsFailure)
            return Result<string>.Failure(recorded.Errors);

        var published = Publish(path, recorded.Value);
        return published.IsFailure
            ? Result<string>.Failure(published.Errors)
            : Result<string>.Success(configurationDirectory);
    }

    // Reads the manifest and returns it with this application's directory set, leaving every other
    // section, and everything else in this one, exactly as it was. Both entry points share it, so
    // what a write preserves cannot differ between them.
    private static Result<ConfigurationManifest> Record(string path, string applicationName, string configurationDirectory)
    {
        var existing = ConfigurationManifestReader.Read(path);
        if (existing.IsFailure)
            return Result<ConfigurationManifest>.Failure(existing.Errors);

        var manifest = existing.Value ?? new ConfigurationManifest { Applications = new Dictionary<string, ApplicationManifestEntry>(StringComparer.Ordinal) };
        var applications = new Dictionary<string, ApplicationManifestEntry>(manifest.Applications!, StringComparer.Ordinal);
        var section = applications.GetValueOrDefault(applicationName) ?? new ApplicationManifestEntry();
        applications[applicationName] = section with { ConfigurationDirectory = configurationDirectory };

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
            var content = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.ConfigurationManifest);
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
