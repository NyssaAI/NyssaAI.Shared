---
feature: F-202
id: UC-001
status: complete
---

# UC-001 Degrade to Null Logger Provider When Log File Cannot Be Opened

## Actor

File Provider Factory (`LoggingBuilderExtensions.AddFile` -> `JsonFileLoggerProvider.TryCreate` -> `LogFile.Initialize`).

## Goal

Prevent an invalid or unwritable log file path from crashing application startup by reporting the reason on standard error and substituting `NullLoggerProvider.Instance` for that file destination while keeping all other configured logging destinations active.

## Starting Context

An application has registered a file destination (`AddNyssaLogging(filePath)`, `AddNyssaJsonFile(filePath)`, or `WithRotatingJsonLog(filePath)`), and the `ServiceProvider` / `ILoggerFactory` is resolving `ILoggerProvider` singletons for the first time.

## Entry Route

Invoked when the DI container executes the singleton factory registered in `LoggingBuilderExtensions.AddFile`, which calls `JsonFileLoggerProvider.TryCreate(destination, clock, out var error, minimumLevel)`.

## Trigger

First resolution of `ILoggerFactory` or `ILogger<T>` from the dependency injection container when the target file path has no directory component or cannot be created/opened on disk.

## Process

```text
LoggingBuilderExtensions resolves a file provider whose path cannot be opened
+-- Starting context: Singleton ILoggerProvider factory invoked for destination path
+-- Entry route: JsonFileLoggerProvider.TryCreate(destination, clock, out error, minimumLevel)
+-- Trigger: Host or application resolves ILoggerFactory / ILogger<T>
+-- Step 1: Validate and initialize LogFile inside TryCreate
|   +-- filePath is null/whitespace -> error = "a log file path is required", return null
|   \-- Construct LogFile(filePath, timeProvider) and call logFile.Initialize()
|       +-- Path.GetDirectoryName(_logPath) is null or empty (e.g., "relative.log")
|       |   \-- Return error = "Invalid log path: relative.log"
|       +-- Directory creation or OpenAppend throws IOException / UnauthorizedAccessException
|       |   \-- Catch non-OutOfMemoryException and return error = "Failed to initialize log file: ..."
|       \-- Initialization failed (error != null) -> Dispose logFile and return null from TryCreate
+-- Step 2: Report diagnostic and substitute NullLoggerProvider in AddFile
|   +-- Emit StderrDiagnostics.Error(destination, $"file logging disabled: {error}")
|   \-- Return NullLoggerProvider.Instance to ILoggerFactory
\-- Success: Application starts normally and logs through all remaining working providers
```

## Step Details

### Step 1: Validate and initialize `LogFile` inside `TryCreate`
- When `logFile.Initialize()` returns a non-null error message, `TryCreate` immediately disposes the `LogFile` instance and returns `null` rather than throwing an exception into the DI container's activation pipeline.

### Step 2: Report diagnostic and substitute `NullLoggerProvider` in `AddFile`
- Returning `NullLoggerProvider.Instance` satisfies `ILoggerFactory`'s `IEnumerable<ILoggerProvider>` resolution contract with a zero-overhead no-op provider for the broken path while preserving working sibling providers (such as `stderr`, `stdout`, OpenTelemetry, or a second valid `AddNyssaJsonFile` path).

## Success

An `erro: <destination>\n      file logging disabled: <error>` diagnostic is printed to `stderr`, `NullLoggerProvider.Instance` takes the place of the unusable file destination, and the application continues running and logging through all other configured destinations.

## Failure Cases

- `OutOfMemoryException` during initialization: allowed to propagate without being caught.

## Consistency Check

- Unique coverage: Covers startup initialization failure handling in `JsonFileLoggerProvider.TryCreate` and fallback to `NullLoggerProvider.Instance`.
- Closest related use cases: `UC-002` covers mid-run rotation and reopen failures after startup initialization has already succeeded; `UC-003` covers provider disposal.
- Contradictions: None found.

## Acceptance

- Calling `JsonFileLoggerProvider.TryCreate("relative.log", TimeProvider.System, out var error)` returns `null` and populates `error` with `"Invalid log path: relative.log"`.
- Registering both `.AddNyssaJsonFile("relative.log")` and `.AddNyssaJsonFile(workspace.LogPath)` emits `erro: relative.log` (`file logging disabled`) on `stderr` and still writes logged records to `workspace.LogPath`.
- Verified by `JsonFileLoggerProviderTests.TryCreate_WithAPathThatHasNoDirectory_ReportsTheReasonAndCreatesNothing`, `JsonFileLoggerProviderTests.AddNyssaJsonFile_WhenTheFileCannotBeOpened_ReportsOnStderrAndKeepsLoggingUsable`, and `LoggingTutorials.Keep_Running_When_The_Log_File_Cannot_Be_Opened`.
