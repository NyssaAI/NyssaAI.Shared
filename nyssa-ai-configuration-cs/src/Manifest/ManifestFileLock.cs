using System.Diagnostics;
using System.Text.Json;
using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// Manages exclusive sidecar file locking (<c>&lt;manifest&gt;.lock</c>) and atomic temporary-file
/// publication for configuration manifests.
/// </summary>
internal static class ManifestFileLock
{
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

    internal static Result Publish(string path, ConfigurationManifest manifest)
    {
        var created = ProtectedFileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        if (created.IsFailure)
            return created;

        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var prepared = ConfigurationManifestWriter.PrepareForWrite(manifest);
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

    internal static Result<FileStream> Acquire(string path, TimeSpan lockTimeout)
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
