using System.Diagnostics;
using System.Text.Json;
using NyssaAI.Results;

using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// ConfigurationDocumentLocalFile persists one namespace as a JSON file, serializing writers through an
/// in-process gate and a cross-process lock file, and replacing the file atomically.
/// </summary>
internal sealed class ConfigurationDocumentLocalFile : IConfigurationDocument
{
    /// <summary>How long <see cref="ReadAsync"/> and <see cref="ModifyAsync"/> wait for the lock.</summary>
    /// <remarks>
    /// Long enough that a writer publishing a document never loses a race it should have won, short
    /// enough that a caller learns something is wrong rather than hanging. A caller that genuinely
    /// wants to wait longer passes its own.
    /// </remarks>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private readonly string _lockPath;
    private readonly TimeSpan _lockTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a document over one JSON file with the default lock timeout.</summary>
    /// <param name="path">The path of the file holding this namespace's entries.</param>
    public ConfigurationDocumentLocalFile(string path)
        : this(path, DefaultLockTimeout)
    {
    }

    private ConfigurationDocumentLocalFile(string path, TimeSpan lockTimeout)
    {
        _path = path;
        _lockPath = path + ".lock";
        _lockTimeout = lockTimeout;
    }

    /// <summary>Creates a document over one JSON file, failing when the lock timeout is not positive.</summary>
    /// <param name="path">The path of the file holding this namespace's entries.</param>
    /// <param name="lockTimeout">
    /// How long to wait for the cross-process lock before failing; <see cref="DefaultLockTimeout"/>
    /// when null.
    /// </param>
    public static Result<ConfigurationDocumentLocalFile> Create(string path, TimeSpan? lockTimeout = null)
    {
        if (lockTimeout is { } supplied && supplied <= TimeSpan.Zero)
            return Result<ConfigurationDocumentLocalFile>.Failure(LockTimeoutInvalid(supplied));

        return Result<ConfigurationDocumentLocalFile>.Success(new ConfigurationDocumentLocalFile(path, lockTimeout ?? DefaultLockTimeout));
    }

    /// <inheritdoc/>
    public async ValueTask<Result<IReadOnlyDictionary<string, ConfigurationValue>>> ReadAsync(CancellationToken cancellationToken = default)
    {
        // This is the adapter boundary for cancellation: the gate and the lock wait both throw
        // OperationCanceledException, and the store's contract is that nothing throws.
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Failure(Error.Canceled(canceled));
        }

        try
        {
            var locked = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            if (locked.IsFailure)
                return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Failure(locked.Errors);

            await using var lockStream = locked.Value;
            return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<Result> ModifyAsync(
        Func<IDictionary<string, ConfigurationValue>, ValueTask<Result>> change,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
        {
            return Result.Failure(Error.Canceled(canceled));
        }

        try
        {
            var locked = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            if (locked.IsFailure)
                return Result.Failure(locked.Errors);

            await using var lockStream = locked.Value;

            var loaded = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.IsFailure)
                return Result.Failure(loaded.Errors);

            var entries = loaded.Value.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

            var changed = await change(entries).ConfigureAwait(false);
            if (changed.IsFailure)
                return changed;

            return await PublishAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The failure returned when the document file cannot be read.</summary>
    /// <param name="path">The path that could not be read.</param>
    /// <param name="exception">The underlying I/O failure.</param>
    public static Error NotReadable(string path, Exception exception) => Error.FromMessages(
        "The configuration could not be read.",
        $"Reading '{path}' failed: {exception.GetType().Name}: {exception.Message}",
        exception is UnauthorizedAccessException ? ErrorCategory.Forbidden : ErrorCategory.Unexpected);

    /// <summary>The failure returned when the document file is not a well-formed entry document.</summary>
    /// <param name="path">The path that failed to parse.</param>
    /// <param name="exception">The underlying parse failure.</param>
    /// <remarks>
    /// <see cref="ErrorCategory.Validation"/>, distinct from <see cref="NotReadable"/>: a corrupt
    /// file does not become readable on retry, and a caller must be able to tell the two apart.
    /// </remarks>
    public static Error Malformed(string path, JsonException exception) => Error.FromMessages(
        "The configuration file is malformed.",
        $"Reading '{path}' failed validation: {exception.Message}",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the document file cannot be written.</summary>
    /// <param name="path">The path that could not be written.</param>
    /// <param name="exception">The underlying I/O failure.</param>
    public static Error NotWritable(string path, Exception exception) => Error.FromMessages(
        "The configuration could not be saved.",
        $"Writing '{path}' failed: {exception.GetType().Name}: {exception.Message}",
        exception is UnauthorizedAccessException ? ErrorCategory.Forbidden : ErrorCategory.Unexpected);

    /// <summary>The failure returned when the namespace lock could not be taken in time.</summary>
    /// <param name="path">The document whose lock was contended.</param>
    /// <param name="waited">How long the caller waited.</param>
    /// <remarks>
    /// <see cref="ErrorCategory.Unavailable"/>: the holder is another writer finishing its own
    /// read-modify-write, so the same attempt can succeed on a retry.
    /// </remarks>
    public static Error LockNotAcquired(string path, TimeSpan waited) => Error.FromMessages(
        "The configuration is in use by something else. Try again.",
        $"configuration: could not take the lock on '{path}' within {waited.TotalSeconds:0.###}s",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when a lock timeout is zero or negative.</summary>
    /// <param name="timeout">The timeout that was supplied.</param>
    public static Error LockTimeoutInvalid(TimeSpan timeout) => Error.FromMessages(
        "The configuration lock timeout is not valid.",
        $"configuration: the lock timeout must be positive; {timeout} was supplied",
        ErrorCategory.Validation);

    // Writes to a temporary file in the same directory and renames it over the document, so a
    // reader never sees a torn write and a crash mid-write leaves the previous document intact.
    // The temporary file is removed on every exit so a failed publish leaves nothing behind.
    private async ValueTask<Result> PublishAsync(Dictionary<string, ConfigurationValue> entries, CancellationToken cancellationToken)
    {
        var created = ProtectedFileSystem.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (created.IsFailure)
            return created;

        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var written = await ProtectedFileSystem.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(entries, ConfigurationJsonContext.Default.DictionaryStringConfigurationValue),
                cancellationToken).ConfigureAwait(false);
            if (written.IsFailure)
                return written;

            // This is the adapter boundary: the file system throws here and nowhere above it.
            try
            {
                File.Move(temporary, _path, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Result.Failure(NotWritable(_path, exception));
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

    // Bounded on purpose. This waited forever, so a lock another process never released -- or one
    // held by a writer stuck on something else -- hung the caller with no error and no timeout,
    // which is indistinguishable from the machine being slow.
    //
    // A lock file left behind by a crashed process is not itself a lock: the exclusive share mode
    // below is a handle the operating system releases when that process dies, so there is nothing
    // stale to reap and no risk of breaking a lock that is genuinely held.
    private async ValueTask<Result<FileStream>> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var created = ProtectedFileSystem.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
        if (created.IsFailure)
            return Result<FileStream>.Failure(created.Errors);

        // This is the adapter boundary: the lock file throws for a read-only or directory-shaped
        // path, and the wait throws on cancellation. Neither crosses into the store.
        try
        {
            var start = Stopwatch.GetTimestamp();
            var backoff = TimeSpan.FromMilliseconds(5);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return Result<FileStream>.Success(new FileStream(
                        _lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous));
                }
                catch (IOException)
                {
                    var elapsed = Stopwatch.GetElapsedTime(start);
                    if (elapsed >= _lockTimeout)
                    {
                        return Result<FileStream>.Failure(LockNotAcquired(_path, elapsed));
                    }

                    // Backs off rather than spinning at a fixed rate, and never sleeps past the
                    // deadline, so the wait ends when it says it will.
                    var remaining = _lockTimeout - elapsed;
                    await Task.Delay(backoff < remaining ? backoff : remaining, cancellationToken).ConfigureAwait(false);
                    backoff = backoff < TimeSpan.FromMilliseconds(50) ? backoff * 2 : backoff;
                }
            }
        }
        catch (OperationCanceledException canceled)
        {
            return Result<FileStream>.Failure(Error.Canceled(canceled));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result<FileStream>.Failure(NotWritable(_lockPath, exception));
        }
    }

    private async ValueTask<Result<IReadOnlyDictionary<string, ConfigurationValue>>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Success(
                new Dictionary<string, ConfigurationValue>(StringComparer.Ordinal));

        // This is the adapter boundary: System.Text.Json and the file system throw here, and
        // nothing above this method ever sees those exceptions.
        try
        {
            await using var stream = File.OpenRead(_path);
            var entries = await JsonSerializer
                .DeserializeAsync(stream, ConfigurationJsonContext.Default.DictionaryStringConfigurationValue, cancellationToken)
                .ConfigureAwait(false);
            return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Success(
                entries ?? new Dictionary<string, ConfigurationValue>(StringComparer.Ordinal));
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Failure(Malformed(_path, exception));
        }
        catch (OperationCanceledException canceled)
        {
            return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Failure(Error.Canceled(canceled));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyDictionary<string, ConfigurationValue>>.Failure(NotReadable(_path, exception));
        }
    }
}
