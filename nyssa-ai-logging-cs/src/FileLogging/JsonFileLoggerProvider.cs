using Microsoft.Extensions.Logging;

namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// JsonFileLoggerProvider writes structured JSON log records to the rotating log file owned by
/// <see cref="LogFile"/>. It is a plain <see cref="ILoggerProvider"/>: the host's logger factory
/// owns composition, applies the level filters, and supplies the category name.
/// </summary>
internal sealed class JsonFileLoggerProvider : ILoggerProvider
{
    private readonly LogFile _logFile;
    private readonly TimeProvider _timeProvider;
    private readonly LogLevel _minimumLevel;
    private bool _disposed;

    private JsonFileLoggerProvider(LogFile logFile, TimeProvider timeProvider, LogLevel minimumLevel)
    {
        _logFile = logFile;
        _timeProvider = timeProvider;
        _minimumLevel = minimumLevel;
    }

    /// <summary>
    /// Creates a file provider, or returns null when the log file cannot be opened.
    /// </summary>
    /// <param name="filePath">The log file to write.</param>
    /// <param name="timeProvider">The clock every record is timestamped from.</param>
    /// <param name="error">Receives why the provider could not be created, or null on success.</param>
    /// <param name="minimumLevel">An optional minimum applied only to this provider's file.</param>
    /// <remarks>
    /// It returns null rather than throwing because logging must not take the host process down.
    /// A caller that cannot open the file reports the reason and registers nothing, so the
    /// application keeps whatever other destinations it configured.
    /// </remarks>
    public static JsonFileLoggerProvider? TryCreate(
        string filePath,
        TimeProvider timeProvider,
        out string? error,
        LogLevel? minimumLevel = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "a log file path is required";
            return null;
        }

        var logFile = new LogFile(filePath, timeProvider);
        error = logFile.Initialize();
        if (error is not null)
        {
            logFile.Dispose();
            return null;
        }

        return new JsonFileLoggerProvider(logFile, timeProvider, minimumLevel ?? LogLevel.Trace);
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        new JsonFileLogger(categoryName, _logFile, _timeProvider, _minimumLevel);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logFile.Dispose();
    }
}
