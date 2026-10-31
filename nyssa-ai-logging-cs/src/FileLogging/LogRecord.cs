namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// LogRecord is one structured log entry as it is written to the JSON log file.
/// </summary>
internal sealed record LogRecord
{
    /// <summary>Gets the ISO-8601 round-trip timestamp, supplied by the writer's clock.</summary>
    public required string Timestamp { get; init; }

    /// <summary>Gets the severity name.</summary>
    public required string Level { get; init; }

    /// <summary>Gets the formatted log message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the logger category that produced the entry.</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>Gets the operation name, when the caller supplies one.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Gets the formatted exception, or null when the entry carries none.</summary>
    public string? Exception { get; init; }

    /// <summary>Gets the structured fields captured from the logging state.</summary>
    public IReadOnlyDictionary<string, object?> AdditionalFields { get; init; } =
        new Dictionary<string, object?>();
}
