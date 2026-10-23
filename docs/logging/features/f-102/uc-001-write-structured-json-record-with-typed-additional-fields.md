---
feature: F-102
id: UC-001
status: complete
---

# UC-001 Write Structured JSON Record with Typed Additional Fields

## Actor

Application Service / `ILogger<T>` Consumer (`JsonFileLogger.Log` -> `LogFile.WriteRecord`).

## Goal

Write a structured log entry via standard `ILogger<T>` message-template placeholders so that the active JSONL file records a single UTF-8 JSON line with a fixed seven-property schema and preserves JSON scalar types inside `AdditionalFields` without reflection serialization.

## Starting Context

`JsonFileLoggerProvider` is initialized and active for a log file path, and an application class holds an injected `ILogger<T>` (or `ILogger` created with a category name).

## Entry Route

Application code calls `logger.LogInformation("Order {OrderId} accepted for {Amount}...", ...)` (or any `ILogger.Log<TState>` overload) at an enabled `LogLevel`.

## Trigger

An enabled `ILogger.Log<TState>` invocation reaches `JsonFileLogger.Log`.

## Process

```text
Application logs a structured message to the JSON file
+-- Starting context: JsonFileLogger is active for categoryName; logLevel is enabled
+-- Entry route: ILogger.Log<TState> called with message template and placeholder arguments
+-- Trigger: Enabled log call reaches JsonFileLogger.Log
+-- Step 1: Extract structured fields from TState via ExtractFields(state)
|   +-- state is not IEnumerable<KeyValuePair<string, object>> -> Return empty dictionary (NoFields)
|   \-- state is IEnumerable<KeyValuePair<string, object>>
|       +-- Iterate key-value pairs
|       +-- Key == "{OriginalFormat}" -> Skip (already represented in formatted Message)
|       \-- Other keys -> Copy into AdditionalFields dictionary
+-- Step 2: Construct LogRecord
|   +-- Timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("O")
|   +-- Level = logLevel.ToString()
|   +-- Message = formatter(state, exception)
|   +-- Module = _categoryName (e.g., typeof(T).FullName)
|   +-- Operation = eventId.Name ?? string.Empty
|   +-- Exception = exception?.ToString()
|   \-- AdditionalFields = extracted dictionary
+-- Step 3: Serialize LogRecord via Utf8JsonWriter (LogFile.SerializeRecord)
|   +-- Write fixed top-level properties in order: Timestamp, Level, Message, Module, Operation, Exception
|   +-- Write AdditionalFields object via WriteField(json, key, value)
|   |   +-- null -> WriteNull
|   |   +-- string, DateTime, DateTimeOffset, Guid -> WriteString
|   |   +-- bool -> WriteBoolean
|   |   +-- int, long, double, float, decimal -> WriteNumber
|   |   \-- Any other type (enums, objects) -> WriteString(value.ToString() ?? string.Empty)
|   \-- Return UTF-8 byte array
+-- Step 4: Append and flush record under lock
|   +-- Acquire _lock on LogFile (check rotation threshold first if applicable)
|   +-- Seek _fileStream to SeekOrigin.End
|   +-- Write UTF-8 JSON bytes followed by (byte)'\n' and call _fileStream.Flush()
|   \-- Update _bytesWritten = _fileStream.Position
\-- Success: Single-line UTF-8 JSON object with typed AdditionalFields is flushed to the log file
```

## Step Details

### Step 3: Serialize `LogRecord` via `Utf8JsonWriter` (`LogFile.SerializeRecord`)
- Using `Utf8JsonWriter` directly with an explicit type switch in `WriteField` avoids reflection-based `JsonSerializer` metadata lookup, ensuring 100% compatibility with Native AOT and assembly trimming (`IsAotCompatible`, `IsTrimmable`).

### Step 4: Append and flush record under lock
- On Windows, `FileMode.Append` only seeks to the end when the file is first opened and thereafter uses the `FileStream`'s own position. Explicitly calling `_fileStream.Seek(0, SeekOrigin.End)` before every write ensures that two writers sharing a path append cleanly on both Windows and Unix and sets `_fileStream.Position` to the true file length.

## Success

One complete JSON object line terminated by `\n` is appended and flushed to the active log file with keys in the exact order `Timestamp`, `Level`, `Message`, `Module`, `Operation`, `Exception`, `AdditionalFields`.

## Failure Cases

- `formatter` throws or `Utf8JsonWriter` rejects a non-finite floating-point value (`NaN` / `Infinity`): caught by `JsonFileLogger.Log` or `LogFile.WriteRecord` (`when (ex is not OutOfMemoryException)`), reported to `stderr` via `StderrDiagnostics.Error`, and that single record is dropped while the file stream stays open for subsequent writes.

## Consistency Check

- Unique coverage: Covers structured placeholder extraction, `{OriginalFormat}` omission, `Utf8JsonWriter` field typing, and synchronous end-seeking append.
- Closest related use cases: `UC-002` covers `EventId.Name` (`Operation`) and `Exception` formatting; `UC-003` covers host and per-file `LogLevel` filtering before serialization.
- Contradictions: None found.

## Acceptance

- The written JSON object properties appear in exact order: `["Timestamp", "Level", "Message", "Module", "Operation", "Exception", "AdditionalFields"]`.
- `int` (`1042`) and `decimal` (`39.50m`) remain JSON numbers, `bool` remains a JSON boolean, enums (`ShippingService.Express`) serialize as `"Express"`, and `{OriginalFormat}` is absent from `AdditionalFields`.
- Verified by `JsonFileLoggerProviderTests.CreateLogger_WritesTheDocumentedJsonKeysInOrder`, `JsonFileLoggerProviderTests.CreateLogger_WithStructuredData_PreservesTheFieldsInTheJsonRecord`, and `LoggingTutorials.Log_Structured_Messages`.
