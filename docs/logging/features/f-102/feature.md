---
id: F-102
phase: 1
status: complete
---

# F-102 Structured JSON Record Serialization

## Summary

Implements `JsonFileLoggerProvider`, `JsonFileLogger`, `LogRecord`, and `LogFile.WriteRecord` to filter records by per-file `LogLevel`, extract structured message-template parameters and `EventId.Name`, and serialize each entry as a single UTF-8 JSON line using `Utf8JsonWriter` with synchronous stream flushing.

## User Need

Developers, AI diagnostic tools, and log shippers need local log files where every line is a self-contained, deterministically ordered JSON object containing an ISO-8601 UTC timestamp, severity level, formatted message, class/category module name, operation name, exception details, and typed structured fields — compatible with Native AOT and trimming without reflection serialization.

## Use Cases

- [UC-001: Write Structured JSON Record with Typed Additional Fields](uc-001-write-structured-json-record-with-typed-additional-fields.md)
- [UC-002: Record Operation Name and Exception Details](uc-002-record-operation-name-and-exception-details.md)
- [UC-003: Filter Records Using Host and Per-File Minimum Levels](uc-003-filter-records-using-host-and-per-file-minimum-levels.md)

## Scope

- Implementing `ILoggerProvider.CreateLogger(categoryName)` in `JsonFileLoggerProvider` to return a `JsonFileLogger` bound to `categoryName`, shared `LogFile`, `TimeProvider`, and per-file `_minimumLevel`.
- Enforcing per-file level filtering via `JsonFileLogger.IsEnabled(logLevel)` (`logLevel != LogLevel.None && logLevel >= _minimumLevel`) that narrows — and never widens — the host `ILoggerFactory` filter.
- Extracting structured key-value pairs from `TState` when it implements `IEnumerable<KeyValuePair<string, object>>`, omitting the `{OriginalFormat}` template key and populating `LogRecord.AdditionalFields`.
- Mapping `EventId.Name` (or `string.Empty` when null) to `LogRecord.Operation` and `exception?.ToString()` (or `null`) to `LogRecord.Exception`.
- Serializing `LogRecord` via `Utf8JsonWriter` in exact property order: `Timestamp` (UTC ISO-8601 round-trip `"O"`), `Level`, `Message`, `Module`, `Operation`, `Exception`, and `AdditionalFields`.
- Preserving JSON scalar types in `LogFile.WriteField` for `null`, `string`, `bool`, `int`, `long`, `double`, `float`, and `decimal`; writing ISO-8601 strings for `DateTime` and `DateTimeOffset` and formatted strings for `Guid`; and falling back to `value.ToString() ?? string.Empty` for enums and other types.
- Seeking to `SeekOrigin.End`, writing the UTF-8 JSON bytes followed by `(byte)'\n'`, flushing the `FileStream` synchronously under `_lock`, and updating `_bytesWritten = _fileStream.Position`.

## Out of Scope

- Reflection-based deep object graph or collection serialization in `AdditionalFields`.
- Capturing `ILogger.BeginScope<TState>` ambient state (`BeginScope` returns `null`).
- Automatic redaction or masking of sensitive parameter values.
- Background asynchronous queue buffering (writes flush synchronously before `Log<TState>` returns).

## Inputs

- `ILogger.Log<TState>` parameters: `LogLevel`, `EventId`, `TState` (structured state pairs), `Exception?`, and `formatter` callback.
- Category name (`string`) supplied by `ILoggerFactory.CreateLogger`.
- UTC timestamp from `TimeProvider.GetUtcNow()`.

## Outputs

- One UTF-8 JSON object line terminated with `\n` appended and flushed to the active `FileStream`.
- Updated `_bytesWritten` byte counter on `LogFile`.
- `StderrDiagnostics.Error` diagnostic if formatting or stream writing throws a non-`OutOfMemoryException` exception.

## Acceptance

- Every written JSON line enumerates keys in the exact order: `["Timestamp", "Level", "Message", "Module", "Operation", "Exception", "AdditionalFields"]`.
- `Module` matches the `ILogger<T>` category name (e.g., `typeof(OrderService).FullName`), `Operation` matches `EventId.Name` (or `""`), and `Exception` is `null` or `exception.ToString()`.
- `AdditionalFields` preserves JSON numbers for `int`, `long`, `float`, `double`, and `decimal`, booleans for `bool`, strings for `string`/`DateTime`/`DateTimeOffset`/`Guid`/enums, `null` for null values, and omits `{OriginalFormat}`.
- Records below either the host minimum level or the file's `minimumLevel` are not written; a lower file `minimumLevel` never overrides a higher host minimum level.
- Two `LogFile` instances opened on the same path with `FileShare.ReadWrite | FileShare.Delete` both append without overwriting each other on Windows and Unix because `WriteRecord` seeks to `SeekOrigin.End` before writing.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- `F-001` (`OwnerOnlyAccess` for file permissions).
- `F-002` (`StderrDiagnostics` for write/format failure reporting).
