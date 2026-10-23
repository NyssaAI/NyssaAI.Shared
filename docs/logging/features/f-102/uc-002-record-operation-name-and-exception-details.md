---
feature: F-102
id: UC-002
status: complete
---

# UC-002 Record Operation Name and Exception Details

## Actor

Application Service / `ILogger<T>` Consumer (`JsonFileLogger.Log` -> `LogFile.WriteRecord`).

## Goal

Capture an explicit operation identifier (`EventId.Name`) in the JSON `Operation` field and full exception diagnostics (`exception.ToString()`) in the `Exception` field so downstream log queries and AI analysis tools can filter by operation name and inspect complete stack traces.

## Starting Context

An application class holds an `ILogger<T>` connected to a `JsonFileLoggerProvider` and logs either a named operation via `new EventId(id, name)` or an error carrying an `Exception` instance.

## Entry Route

Application code invokes `logger.LogInformation(new EventId(10, "OrderPlaced"), ...)` or `logger.LogError(exception, "Could not save order {OrderId}", orderId)`.

## Trigger

An enabled `ILogger.Log<TState>` call supplying an `EventId` and/or an `Exception` reaches `JsonFileLogger.Log`.

## Process

```text
JsonFileLogger captures Operation and Exception metadata
+-- Starting context: Enabled log call arrives at JsonFileLogger.Log with eventId and exception parameters
+-- Entry route: ILogger.Log<TState>(logLevel, eventId, state, exception, formatter)
+-- Trigger: Application logs with EventId and/or Exception
+-- Step 1: Map EventId to Operation field
|   +-- eventId.Name is non-null -> Operation = eventId.Name (numeric eventId.Id is ignored)
|   \-- eventId.Name is null -> Operation = string.Empty ("")
+-- Step 2: Map Exception to Exception field
|   +-- exception is non-null -> Exception = exception.ToString() (includes type, message, inner exceptions, and stack trace)
|   \-- exception is null -> Exception = null
+-- Step 3: Serialize Operation and Exception into the JSON record
|   +-- Utf8JsonWriter writes "Operation": "<string>"
|   \-- Utf8JsonWriter writes "Exception": "<string>" or JSON null
\-- Success: JSON line contains Operation string and Exception details (or null)
```

## Step Details

### Step 1: Map `EventId` to `Operation` field
- Only `eventId.Name` is persisted (`Operation = eventId.Name ?? string.Empty`); the numeric `eventId.Id` is intentionally omitted so log queries filter on human-readable operation names like `"OrderAccepted"` or `"Checkout"`.

### Step 2: Map `Exception` to `Exception` field
- `Utf8JsonWriter.WriteString("Exception", record.Exception)` writes a JSON string when `record.Exception` is non-null and automatically writes JSON `null` (`"Exception":null`) when `record.Exception` is `null`.

## Success

The resulting JSON line stores `eventId.Name` (or `""`) in `"Operation"` and either the full `exception.ToString()` output or JSON `null` in `"Exception"`.

## Failure Cases

- Custom `Exception.ToString()` implementation throws a non-`OutOfMemoryException` error: caught by `JsonFileLogger.Log`, reported via `StderrDiagnostics.Error(_categoryName, $"failed to log to file: {ex.Message}")`, and the record is dropped without crashing the caller.

## Consistency Check

- Unique coverage: Covers mapping `EventId.Name` to `Operation` and `Exception` to nullable `Exception` text in the JSON record.
- Closest related use cases: `UC-001` covers general record layout and `AdditionalFields`; `UC-003` covers level filtering.
- Contradictions: None found.

## Acceptance

- Passing `new EventId(7, "Checkout")` produces `"Operation":"Checkout"`; omitting an `EventId` name produces `"Operation":""`.
- Passing no exception produces `"Exception":null` (`JsonValueKind.Null`); passing `new InvalidOperationException("Test error")` writes the full `InvalidOperationException` string into `"Exception"`.
- Verified by `JsonFileLoggerProviderTests.CreateLogger_WritesTheDocumentedJsonKeysInOrder` and `LogFileTests.WriteRecord_WithAnException_WritesTheExceptionText`.
