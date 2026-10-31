namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// LogFile owns the rotating JSON log file: it creates the file restricted to its owner,
/// appends one JSON object per line, and rotates once the file passes the trigger size.
/// Writes are serialized by an internal lock; every failure is reported to stderr rather
/// than thrown, because logging must not take the host process down.
/// </summary>
internal sealed class LogFile : IDisposable
{
    /// <summary>Size in megabytes at which the active file is rotated.</summary>
    public const int RotationTriggerMB = 10;

    /// <summary>Number of rotated backups kept alongside the active file.</summary>
    public const int BackupRetention = 5;

    /// <summary>Age in days beyond which a rotated backup is deleted.</summary>
    public const int RetentionDays = 30;

    private const long BytesPerMegabyte = 1024L * 1024L;
    private static readonly TimeSpan ReopenRetryDelay = TimeSpan.FromSeconds(1);

    private readonly string _logPath;
    private readonly long _rotationTriggerBytes;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private FileStream? _fileStream;
    private long _bytesWritten;
    private long _rotationRetryAtBytes;
    private DateTimeOffset? _reopenRetryAt;
    private bool _disposed;

    /// <summary>Open a log file at the path the application supplied.</summary>
    /// <param name="logPath">The log file path.</param>
    /// <param name="timeProvider">The clock the retention window is measured against.</param>
    public LogFile(string logPath, TimeProvider? timeProvider = null)
        : this(logPath, RotationTriggerMB * BytesPerMegabyte, timeProvider)
    {
    }

    /// <summary>
    /// Construct with an explicit rotation trigger. Tests use this to exercise rotation
    /// without writing ten megabytes.
    /// </summary>
    // TimeProvider.System is the fallback rather than a dependency reached through a static:
    // the composition root names the clock it wants, and a caller that does not care gets the
    // system one without every call site having to say so.
    internal LogFile(string logPath, long rotationTriggerBytes, TimeProvider? timeProvider = null)
    {
        _logPath = logPath;
        _rotationTriggerBytes = rotationTriggerBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Create the directory and file if needed, open the file for appending, and prune
    /// backups past the retention window. Returns an error message on failure, or null on
    /// success.
    /// </summary>
    public string? Initialize()
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(_logPath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                return $"Invalid log path: {_logPath}";
            }

            OwnerOnlyAccess.CreateDirectory(directoryPath);

            lock (_lock)
            {
                _fileStream = OpenAppend(_logPath);
                _bytesWritten = _fileStream.Length;

                // Tightens a file left behind by an earlier run; a file this call created
                // already carries the right mode from OpenAppend.
                OwnerOnlyAccess.ApplyToFile(_logPath);

                // A low-volume consumer may never rotate again, so the age cutoff has to be
                // applied at startup as well as on rotation.
                DeleteExpiredBackups();
            }

            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"Failed to initialize log file: {ex.Message}";
        }
    }

    /// <summary>Write one JSON record as a single line, rotating first if the file is full.</summary>
    /// <param name="record">The record to serialize.</param>
    public void WriteRecord(LogRecord record)
    {
        lock (_lock)
        {
            // Checked inside the lock so a pending recovery cannot reopen after disposal.
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_fileStream is null)
                {
                    // Only a failed rotation schedules recovery; writing before Initialize
                    // still does nothing. Back off while access remains unavailable.
                    if (_reopenRetryAt is null || _timeProvider.GetUtcNow() < _reopenRetryAt.Value)
                    {
                        return;
                    }

                    TryReopenAfterFailedRotation();
                    if (_fileStream is null)
                    {
                        return;
                    }
                }

                if (_bytesWritten > _rotationTriggerBytes && _bytesWritten >= _rotationRetryAtBytes)
                {
                    Rotate();
                }

                if (_fileStream is null)
                {
                    return;
                }

                var line = LogRecordSerializer.Serialize(record);

                // On Windows, FileMode.Append only seeks to the end once, at open, and then
                // writes at a position this stream tracks itself; a second writer that has
                // extended the file since would be overwritten. Seeking to the end before
                // each record makes both platforms append, and the position afterwards is
                // the true file size, which is what the rotation trigger should see.
                _fileStream.Seek(0, SeekOrigin.End);
                _fileStream.Write(line);
                _fileStream.WriteByte((byte)'\n');
                _fileStream.Flush();

                _bytesWritten = _fileStream.Position;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                StderrDiagnostics.Error(_logPath, $"failed to write log record: {ex.Message}");
            }
        }
    }

    /// <summary>Close and release the file. Safe to call more than once.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _fileStream?.Dispose();
            _fileStream = null;
        }
    }

    /// <summary>
    /// Open for appending, sharing the file with other writers and with deletion. Two loggers
    /// on one path (two instances in a process, or two processes) then behave the same on
    /// every platform, and a reader that permits delete sharing does not block rotation's
    /// rename on Windows. The lock protects writes by this instance only; separate writers
    /// can race both appends and rotation. Use a distinct path for each provider or process.
    /// </summary>
    private static FileStream OpenAppend(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.ReadWrite | FileShare.Delete,
        };

        // Applied only when this call creates the file, which is what closes the window in
        // which a newly created log file would otherwise sit at the process umask.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyAccess.OwnerReadWrite;
        }

        return new FileStream(path, options);
    }

    /// <summary>Archive the active file, shift the backups, and reopen. Caller holds the lock.</summary>
    private void Rotate()
    {
        try
        {
            _fileStream?.Dispose();
            _fileStream = null;

            // Shift backups from the oldest down: .4 -> .5, .3 -> .4, and so on.
            for (var index = BackupRetention - 1; index >= 1; index--)
            {
                var olderPath = $"{_logPath}.{index}";
                var newerPath = $"{_logPath}.{index + 1}";

                if (!File.Exists(olderPath))
                {
                    continue;
                }

                if (File.Exists(newerPath))
                {
                    File.Delete(newerPath);
                }

                File.Move(olderPath, newerPath);
            }

            var archivePath = $"{_logPath}.1";
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            File.Move(_logPath, archivePath);

            DeleteExpiredBackups();

            _fileStream = OpenAppend(_logPath);
            OwnerOnlyAccess.ApplyToFile(_logPath);
            _bytesWritten = 0;
            _rotationRetryAtBytes = 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StderrDiagnostics.Error(_logPath, $"failed to rotate log file: {ex.Message}");

            // Rotation failed partway through; reopen so logging continues rather than
            // going silent for the rest of the process lifetime.
            TryReopenAfterFailedRotation();
        }
    }

    /// <summary>
    /// Reopen after a failed rotation and back off: the file is still over the trigger, so
    /// without a retry threshold every following record would attempt the rotation again
    /// and print the same failure to stderr. If reopening also fails, later writes retry
    /// after a short delay so a temporary outage does not disable logging permanently.
    /// </summary>
    private void TryReopenAfterFailedRotation()
    {
        if (_fileStream is not null)
        {
            return;
        }

        try
        {
            _fileStream = OpenAppend(_logPath);
            _bytesWritten = _fileStream.Length;
            _rotationRetryAtBytes = _bytesWritten + _rotationTriggerBytes;
            _reopenRetryAt = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _reopenRetryAt = _timeProvider.GetUtcNow() + ReopenRetryDelay;
            StderrDiagnostics.Error(_logPath, $"failed to reopen log file: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete rotated backups past the retention window. Caller holds the lock.
    ///
    /// The backup indices are walked explicitly rather than globbed: on Windows the pattern
    /// "name.log.*" also matches "name.log" itself, so a glob here could select the active
    /// log file.
    /// </summary>
    private void DeleteExpiredBackups()
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-RetentionDays);

        for (var index = 1; index <= BackupRetention; index++)
        {
            var path = $"{_logPath}.{index}";

            try
            {
                if (!File.Exists(path) || new FileInfo(path).LastWriteTimeUtc >= cutoff)
                {
                    continue;
                }

                File.Delete(path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                StderrDiagnostics.Warning(path, $"failed to delete expired log backup: {ex.Message}");
            }
        }
    }
}
