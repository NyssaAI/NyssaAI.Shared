---
feature: F-102
id: UC-003
status: complete
---

# UC-003 Filter Records Using Host and Per-File Minimum Levels

## Actor

Logger Pipeline (`ILoggerFactory` -> `JsonFileLogger.IsEnabled` / `JsonFileLogger.Log`).

## Goal

Ensure each JSON file destination writes only records that satisfy both the host's `ILoggerFactory` minimum/category filter rules and the file provider's own `minimumLevel` threshold, without a lower file threshold ever widening the host's filter.

## Starting Context

One or more `JsonFileLoggerProvider` instances are registered with optional `minimumLevel` parameters (defaulting internally to `LogLevel.Trace` when omitted so the host filter governs).

## Entry Route

Application code calls `ILogger.Log<TState>` or `ILogger.IsEnabled(logLevel)`.

## Trigger

A log call is evaluated by `ILoggerFactory` and dispatched to each registered `JsonFileLogger`.

## Process

```text
Logger pipeline evaluates host and per-file LogLevel filters
+-- Starting context: Host ILoggerFactory has its minimum level and filter rules; JsonFileLogger has _minimumLevel
+-- Entry route: ILogger.Log or ILogger.IsEnabled invoked on category logger
+-- Trigger: Application emits a log message at logLevel
+-- Step 1: Host ILoggerFactory evaluates global/category filter rules
|   +-- logLevel is below host minimum level -> Host drops record before calling JsonFileLogger
|   \-- logLevel meets host minimum level -> Host forwards call to JsonFileLogger
+-- Step 2: JsonFileLogger evaluates per-file IsEnabled(logLevel)
|   +-- logLevel == LogLevel.None or logLevel < _minimumLevel
|   |   \-- Return false / exit Log<TState> without formatting or touching LogFile
|   \-- logLevel >= _minimumLevel
|       \-- Format record and call _logFile.WriteRecord(...) (covered by UC-001)
\-- Success: Only records meeting max(hostMinimum, fileMinimum) are written to the file
```

## Step Details

### Step 2: `JsonFileLogger` evaluates per-file `IsEnabled(logLevel)`
- `JsonFileLoggerProvider` stores `minimumLevel ?? LogLevel.Trace` on the provider instance rather than registering a framework-level `LoggerFilterRule` for `typeof(JsonFileLoggerProvider)`. A framework-level provider-type rule would apply across all `JsonFileLoggerProvider` instances and could override (widen) the host's global minimum level instead of strictly narrowing a single file.

## Success

Records below the host minimum or below the specific file's `minimumLevel` are skipped without formatting or disk I/O, while records meeting both thresholds are written to that file.

## Failure Cases

- Caller passes `LogLevel.None` to `Log`: `IsEnabled(LogLevel.None)` returns `false` and the record is ignored.

## Consistency Check

- Unique coverage: Covers runtime two-stage level filtering (`ILoggerFactory` + instance-level `JsonFileLogger.IsEnabled`) and non-widening semantics.
- Closest related use cases: `F-101` / `UC-003` covers registering multiple files with different `minimumLevel` arguments; `UC-001` covers writing records that pass the filter.
- Contradictions: None found.

## Acceptance

- When host minimum is `LogLevel.Warning` and file `minimumLevel` is `LogLevel.Trace` or `LogLevel.Information`, `LogInformation` is not written to the file (`AddNyssaJsonFile_LowerFileMinimum_DoesNotWidenTheHostMinimum`).
- When host minimum is `LogLevel.Information` and file `minimumLevel` is `LogLevel.Warning`, `LogInformation` is filtered out of the file while `LogWarning` is written (`LoggingTutorials.Choose_Which_Records_Reach_The_File`).
- Verified by `JsonFileLoggerProviderTests.CreateLogger_WhenTheFactoryFiltersALevel_NeverReachesTheFile`, `JsonFileLoggerProviderTests.AddNyssaJsonFile_LowerFileMinimum_DoesNotWidenTheHostMinimum`, and `JsonFileLoggerProviderTests.AddNyssaJsonFile_TwoDifferentMinima_EachFileRetainsItsOwnFilter`.
