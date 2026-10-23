---
feature: F-101
id: UC-001
status: complete
---

# UC-001 Configure Standard Error and Optional File Logging in One Call

## Actor

Application Startup / Composition Root (`IServiceCollection.AddNyssaLogging`).

## Goal

Configure `Microsoft.Extensions.Logging` in a single call so all accepted records write to standard error (`stderr`) by default — keeping `stdout` free for command output — and optionally also write to an exact-path rotating JSON log file, while returning `ILoggingBuilder` for further service or provider registration.

## Starting Context

A .NET application or CLI tool has an `IServiceCollection` instance during startup and wants to register logging for `ILogger<T>` consumers without manually configuring `AddLogging` and `ConsoleLoggerOptions`.

## Entry Route

Application startup invokes `services.AddNyssaLogging(filePath, minimumLevel)` from `NyssaAI.Logging.Configuration.LoggingServiceCollectionExtensions`.

## Trigger

Execution of `services.AddNyssaLogging(...)` during dependency injection container setup.

## Process

```text
Application registers Nyssa logging in one call
+-- Starting context: IServiceCollection exists at startup
+-- Entry route: services.AddNyssaLogging(filePath = null, minimumLevel = LogLevel.Information)
+-- Trigger: Application startup composition
+-- Step 1: Validate service collection
|   +-- services is null -> Throw ArgumentNullException
|   \-- services is non-null -> Proceed to AddLogging callback
+-- Step 2: Configure host minimum level and standard error sink
|   +-- Invoke services.AddLogging(builder => ...) synchronously
|   +-- Call builder.SetMinimumLevel(minimumLevel) (defaults to LogLevel.Information)
|   \-- Call builder.WithStdErr() -> builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
+-- Step 3: Optionally attach exact-path JSON file sink
|   +-- filePath is null -> Skip file registration (stderr-only mode; nothing touches disk)
|   \-- filePath is non-null
|       +-- filePath is empty/whitespace -> Throw ArgumentException
|       \-- Register JsonFileLoggerProvider via builder.AddNyssaJsonFile(filePath)
+-- Step 4: Return ILoggingBuilder to caller
|   \-- Caller chains additional destinations or accesses .Services to continue DI registration
\-- Success: Container resolves ILogger<T> that writes accepted records to stderr (and JSON file when named)
```

## Step Details

### Step 2: Configure host minimum level and standard error sink
- Setting `ConsoleLoggerOptions.LogToStandardErrorThreshold = LogLevel.Trace` routes every enabled console log level (`Trace` through `Critical`) to `stderr` instead of `stdout`.

### Step 3: Optionally attach exact-path JSON file sink
- Omitting `filePath` (or passing `null`) leaves only the `stderr` provider registered, ideal for interactive CLI commands, containers, or tests that should not touch the filesystem. Supplying `filePath` registers `AddNyssaJsonFile(filePath)` alongside `stderr`.

## Success

The `IServiceCollection` is configured with `ILoggerFactory` and `ILogger<T>`, routing records at or above `minimumLevel` to `stderr` and (when `filePath` is supplied) to the rotating JSON file, and returns the `ILoggingBuilder`.

## Failure Cases

- `services` is `null`: throws `ArgumentNullException` during registration.
- `filePath` is non-null but empty or whitespace: throws `ArgumentException` during registration.
- `filePath` is non-null but lacks a directory component or cannot be opened when the provider is resolved: reports `file logging disabled` on `stderr` and continues with `stderr` logging (`F-202` / `UC-001`).

## Consistency Check

- Unique coverage: Covers the one-call `IServiceCollection.AddNyssaLogging` bootstrap for `stderr`-only and `stderr` + exact-file setups.
- Closest related use cases: `UC-002` covers chaining `WithRotatingJsonLog`, `WithConsole`, and `AddOpenTelemetry`; `UC-003` covers adding exact-path JSON files directly on `ILoggingBuilder` with per-file minimum levels.
- Contradictions: None found.

## Acceptance

- `services.AddNyssaLogging()` writes `Information` and higher records to `stderr`, filters `Debug` records, and creates no files on disk.
- `services.AddNyssaLogging(workspace.LogPath, LogLevel.Information)` writes the same `Order 1042 placed` record to both `stderr` and `workspace.LogPath`, and `.Services` returns the original `IServiceCollection`.
- Verified by `LoggingTutorials.Configure_Logging_In_One_Call` and `LoggingTutorials.Log_To_Standard_Error_Only`.
