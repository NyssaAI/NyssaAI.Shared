using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// Category-scoped <see cref="ILogger"/> that formats entries and forwards structured records to
/// <see cref="LogFile"/> while applying a file-specific minimum threshold.
/// </summary>
internal sealed class JsonFileLogger : ILogger
{
    private const string OriginalFormatKey = "{OriginalFormat}";
    private static readonly IReadOnlyDictionary<string, object?> NoFields =
        new Dictionary<string, object?>();

    private readonly string _categoryName;
    private readonly LogFile _logFile;
    private readonly TimeProvider _timeProvider;
    private readonly LogLevel _minimumLevel;

    public JsonFileLogger(string categoryName, LogFile logFile, TimeProvider timeProvider, LogLevel minimumLevel)
    {
        _categoryName = categoryName;
        _logFile = logFile;
        _timeProvider = timeProvider;
        _minimumLevel = minimumLevel;
    }

    // This provider tracks no scopes, and the factory never requires a non-null scope.
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // The factory applies its own filters first; this threshold only narrows this file.
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            var activity = Activity.Current;
            var operation = !string.IsNullOrEmpty(eventId.Name)
                ? eventId.Name
                : activity?.OperationName ?? string.Empty;

            _logFile.WriteRecord(new LogRecord
            {
                Timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("O"),
                Level = logLevel.ToString(),
                Message = formatter(state, exception),
                Module = _categoryName,
                Operation = operation,
                Exception = exception?.ToString(),
                AdditionalFields = ExtractFields(state, activity),
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StderrDiagnostics.Error(_categoryName, $"failed to log to file: {ex.Message}");
        }
    }

    /// <summary>
    /// Take the structured fields from the logging state and active OpenTelemetry <see cref="Activity"/>.
    /// The template itself arrives as a field named {OriginalFormat} and is dropped: the formatted message already carries it.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ExtractFields<TState>(TState state, Activity? activity)
    {
        Dictionary<string, object?>? fields = null;

        if (activity is not null)
        {
            fields = new Dictionary<string, object?>();
            foreach (var tag in activity.TagObjects)
            {
                if (!string.IsNullOrEmpty(tag.Key) && !tag.Key.StartsWith("exception.", StringComparison.Ordinal))
                {
                    fields[tag.Key] = tag.Value;
                }
            }

            if (activity.IdFormat == ActivityIdFormat.W3C)
            {
                fields["TraceId"] = activity.TraceId.ToHexString();
                fields["SpanId"] = activity.SpanId.ToHexString();
                if (activity.ParentSpanId != default)
                {
                    fields["ParentSpanId"] = activity.ParentSpanId.ToHexString();
                }
            }
        }

        if (state is IEnumerable<KeyValuePair<string, object>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (pair.Key == OriginalFormatKey)
                {
                    continue;
                }

                fields ??= new Dictionary<string, object?>();
                fields[pair.Key] = pair.Value;
            }
        }

        return fields ?? NoFields;
    }
}
