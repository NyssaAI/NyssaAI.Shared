---
feature: F-101
id: UC-003
status: complete
---

# UC-003 Add Exact-Path JSON File Destinations with Per-File Minimum Levels

## Actor

Application Startup / Composition Root (`ILoggingBuilder.AddNyssaJsonFile`).

## Goal

Add one or more exact-path rotating JSON log files (without a date prefix and without automatically adding console providers) to an existing `ILoggingBuilder`, optionally assigning each file its own `minimumLevel` threshold.

## Starting Context

An application already configures `AddLogging(builder => ...)` (or `AddNyssaLogging()`) and wants to write records to an exact file path (such as `logs/service.log`) or split records across multiple files (such as `logs/service.log` for `Information+` and `logs/errors.log` for `Error+`).

## Entry Route

Application startup invokes `builder.AddNyssaJsonFile(filePath, minimumLevel)` on `ILoggingBuilder`.

## Trigger

Execution of `AddNyssaJsonFile` during logging builder configuration.

## Process

```text
Application registers one or more exact-path JSON file destinations
+-- Starting context: ILoggingBuilder is being configured in AddLogging
+-- Entry route: builder.AddNyssaJsonFile(filePath, minimumLevel = null)
+-- Trigger: Logging setup in application composition root
+-- Step 1: Validate registration arguments
|   +-- builder is null -> Throw ArgumentNullException
|   +-- filePath is null, empty, or whitespace -> Throw ArgumentException
|   \-- Arguments valid -> Proceed to register singleton factory
+-- Step 2: Register independent ILoggerProvider singleton factory
|   +-- Do not register ConsoleLoggerProvider or ConsoleOutputLoggerProvider
|   \-- In factory callback:
|       +-- Resolve TimeProvider from DI (or TimeProvider.System)
|       +-- Keep exact filePath without prepending a UTC date prefix
|       +-- Call JsonFileLoggerProvider.TryCreate(filePath, clock, out error, minimumLevel)
|       |   +-- Succeeds -> Return JsonFileLoggerProvider bound to its own minimumLevel
|       |   \-- Fails (e.g., no directory in filePath) -> Emit StderrDiagnostics.Error and return NullLoggerProvider.Instance
\-- Success: Each registered file destination independently receives and filters records
```

## Step Details

### Step 2: Register independent `ILoggerProvider` singleton factory
- Because `builder.Services.AddSingleton<ILoggerProvider>(...)` is used rather than `TryAddEnumerable` or framework-level provider-type filter rules, calling `AddNyssaJsonFile` multiple times registers multiple distinct `JsonFileLoggerProvider` instances, each holding its own `_minimumLevel` field without overriding sibling file providers or widening the host's minimum level.

## Success

One or more exact-path JSON file providers are registered on the `ILoggingBuilder`, with no unrequested console providers added, and each file enforces its own optional `minimumLevel`.

## Failure Cases

- `builder` is `null`: throws `ArgumentNullException`.
- `filePath` is null, empty, or whitespace: throws `ArgumentException`.
- `filePath` has no directory component (e.g., `"relative.log"`): `LogFile.Initialize()` returns `"Invalid log path: relative.log"`, `AddFile` emits `erro: relative.log` on `stderr`, and substitutes `NullLoggerProvider.Instance` (`F-202` / `UC-001`).

## Consistency Check

- Unique coverage: Covers exact-path `AddNyssaJsonFile` registration without console side effects and multi-file registration on the same `ILoggingBuilder`.
- Closest related use cases: `UC-001` covers `AddNyssaLogging`; `UC-002` covers date-prefixed `WithRotatingJsonLog`; `F-102` / `UC-003` covers runtime level evaluation inside `JsonFileLogger`.
- Contradictions: None found.

## Acceptance

- `services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddNyssaJsonFile(workspace.LogPath))` writes JSON records to `workspace.LogPath` without adding a console provider.
- Registering two `AddNyssaJsonFile` calls with `LogLevel.Information` and `LogLevel.Error` (in either order) delivers `Information` and `Error` records to the first file and only `Error` records to the second file.
- Verified by `LoggingTutorials.Write_Logs_To_A_Json_File`, `LoggingTutorials.Choose_Which_Records_Reach_The_File`, and `JsonFileLoggerProviderTests.AddNyssaJsonFile_TwoDifferentMinima_EachFileRetainsItsOwnFilter`.
