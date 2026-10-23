---
feature: F-101
id: UC-002
status: complete
---

# UC-002 Chain Dated JSON File, Standard Error, Standard Output, and OpenTelemetry

## Actor

Application Startup / Composition Root (`ILoggingBuilder` fluent chain).

## Goal

Chain a UTC-date-prefixed rotating JSON file (`WithRotatingJsonLog`), standard error (`WithStdErr`), independent standard output (`WithConsole`), and third-party providers such as OpenTelemetry (`AddOpenTelemetry`) so each destination receives accepted `ILogger<T>` records once without console option clobbering or duplicate output.

## Starting Context

An application has obtained an `ILoggingBuilder` from `services.AddNyssaLogging()` or `services.AddLogging(...)` and wants to compose multiple terminal, dated-file, and telemetry destinations.

## Entry Route

Application startup chains `.WithRotatingJsonLog(filePath, minimumLevel)`, `.WithStdErr()`, `.WithConsole()`, and `.AddOpenTelemetry(...)` on `ILoggingBuilder`.

## Trigger

Startup logging configuration and subsequent resolution of `ILogger<T>` from the built `ServiceProvider`.

## Process

```text
Application chains dated file, terminal, and OpenTelemetry destinations
+-- Starting context: ILoggingBuilder obtained from AddNyssaLogging() or AddLogging(...)
+-- Entry route: Chained calls to WithRotatingJsonLog, WithStdErr, WithConsole, and AddOpenTelemetry
+-- Trigger: Service registration and first ILogger<T> resolution
+-- Step 1: Register dated rotating JSON file via WithRotatingJsonLog(filePath, minimumLevel)
|   +-- Validate builder != null, filePath not null/whitespace, and Path.GetFileName(filePath) not empty
|   |   \-- Validation fails -> Throw ArgumentNullException or ArgumentException
|   \-- Register singleton ILoggerProvider factory
|       +-- Resolve TimeProvider from DI (or TimeProvider.System)
|       +-- Extract directory from filePath (if empty, use Directory.GetCurrentDirectory())
|       +-- Format opening UTC date as "yyyy.MM.dd" (InvariantCulture)
|       +-- Build destination = Path.Combine(directory, $"{date}-{Path.GetFileName(filePath)}")
|       \-- Create JsonFileLoggerProvider for destination
+-- Step 2: Register or re-affirm standard error via WithStdErr()
|   \-- Configure ConsoleLoggerOptions.LogToStandardErrorThreshold = LogLevel.Trace (idempotent across calls)
+-- Step 3: Register independent standard output via WithConsole()
|   \-- Call builder.Services.TryAddEnumerable(Singleton<ILoggerProvider, ConsoleOutputLoggerProvider>())
|       \-- ConsoleOutputLoggerProvider uses private OutputOptions (LogToStandardErrorThreshold = LogLevel.None)
+-- Step 4: Chain external provider (e.g., AddOpenTelemetry) and emit log record
|   \-- LoggerFactory dispatches each accepted record once to stderr, once to stdout, once to dated JSON file, and once to OpenTelemetry
\-- Success: All chained destinations receive the record without duplication or option clobbering
```

## Step Details

### Step 1: Register dated rotating JSON file via `WithRotatingJsonLog(filePath, minimumLevel)`
- Unlike `AddNyssaJsonFile`, `WithRotatingJsonLog` accepts a bare file name (e.g., `"application-log.json"`) and defaults its directory to `Directory.GetCurrentDirectory()`. The UTC date prefix (`yyyy.MM.dd-`) is evaluated when the provider singleton is created and remains fixed for the lifetime of that provider instance across size rotations.

### Step 3: Register independent standard output via `WithConsole()`
- Calling `AddConsole` twice in `Microsoft.Extensions.Logging` shares one `ConsoleLoggerProvider` and one `ConsoleLoggerOptions` instance. `ConsoleOutputLoggerProvider` (`[ProviderAlias("NyssaConsole")]`) subclasses `ConsoleLoggerProvider` with its own isolated `IOptionsMonitor<ConsoleLoggerOptions>` (`LogToStandardErrorThreshold = LogLevel.None`) and registers via `TryAddEnumerable`, so `WithStdErr()` and `WithConsole()` never interfere or duplicate each other even if called repeatedly.

## Success

Each logged event at or above the active threshold is written once to `yyyy.MM.dd-<fileName>`, once to `stderr`, once to `stdout`, and once to any chained OpenTelemetry exporter.

## Failure Cases

- `builder` is `null`: throws `ArgumentNullException`.
- `filePath` is null, whitespace, or ends with a directory separator (`Path.GetFileName(filePath)` is empty): throws `ArgumentException`.
- Dated file path cannot be opened on disk at runtime: reports `file logging disabled` on `stderr` and continues logging to `stderr`, `stdout`, and OpenTelemetry (`F-202` / `UC-001`).

## Consistency Check

- Unique coverage: Covers fluent chaining of `WithRotatingJsonLog` (`yyyy.MM.dd-` prefix and working-directory default), `WithConsole` (`ConsoleOutputLoggerProvider`), `WithStdErr` idempotency, and `AddOpenTelemetry`.
- Closest related use cases: `UC-001` covers single-call `AddNyssaLogging`; `UC-003` covers exact-path `AddNyssaJsonFile` with per-file level filtering.
- Contradictions: None found.

## Acceptance

- Chaining `AddNyssaLogging().WithRotatingJsonLog(...).WithStdErr().WithConsole().WithStdErr().WithConsole()` writes a logged message exactly once to `stdout`, once to `stderr`, and once to `yyyy.MM.dd-application-log.json`.
- Calling `WithRotatingJsonLog("file.json")` with no directory writes `yyyy.MM.dd-file.json` in `Directory.GetCurrentDirectory()`.
- Chaining `.AddOpenTelemetry(options => options.AddInMemoryExporter(records))` delivers the structured record (`Body` and `Attributes`) to OpenTelemetry alongside the dated JSON file.
- Verified by `LoggingInstallationTests.AddNyssaLogging_ChainedDestinations_WriteOnceToEachDestination`, `LoggingInstallationTests.WithRotatingJsonLog_BareFileName_WritesDatedFileInWorkingDirectory`, and `LoggingTutorials.Chain_Destinations_And_OpenTelemetry`.
