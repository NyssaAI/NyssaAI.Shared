---
feature: F-002
id: UC-001
status: complete
---

# UC-001 Report Internal File and Permission Diagnostics to Standard Error

## Actor

Internal File Logging Components (`LoggingBuilderExtensions`, `JsonFileLoggerProvider`, `LogFile`, `OwnerOnlyAccess` -> `StderrDiagnostics`).

## Goal

Format and write clear, console-consistent error (`erro:`) or warning (`warn:`) diagnostics to standard error whenever an internal file logging operation fails, regardless of the host's configured `LogLevel` filters.

## Starting Context

A file logging component encounters an operational failure during provider creation, permission tightening, record writing, file rotation, file reopening, or expired backup deletion.

## Entry Route

Invoked internally via `StderrDiagnostics.Error(subject, detail)` or `StderrDiagnostics.Warning(subject, detail)`.

## Trigger

An internal operational error or warning condition in `LoggingBuilderExtensions.AddFile`, `JsonFileLogger.Log`, `LogFile`, or `OwnerOnlyAccess`.

## Process

```text
Component reports an internal diagnostic to standard error
+-- Starting context: internal file or permission operation failed with a known subject and detail message
+-- Entry route: Caller invokes StderrDiagnostics.Error(subject, detail) or StderrDiagnostics.Warning(subject, detail)
+-- Trigger: Unwritable file at startup, record write error, rotation/reopen failure, backup delete failure, or permission failure
+-- Step 1: Select four-character severity tag
|   +-- Error called -> levelTag = "erro"
|   \-- Warning called -> levelTag = "warn"
+-- Step 2: Format two-line diagnostic string via StderrDiagnostics.Format
|   \-- Construct "{levelTag}: {subject}{Environment.NewLine}      {detail}"
+-- Step 3: Write formatted string to Console.Error via StderrDiagnostics.WriteLine
|   \-- Console.Error.WriteLine(line) succeeds -> Diagnostic visible on standard error
\-- Success: Two-line diagnostic emitted to standard error without disturbing application execution
```

## Step Details

### Step 2: Format two-line diagnostic string via `StderrDiagnostics.Format`
- The format matches Microsoft's default console logger layout: a four-character lowercase level tag (`erro` or `warn`), a colon and space, the `subject` (either the target file path or logger category name), `Environment.NewLine`, six spaces of indentation, and the `detail` message.

## Success

A formatted two-line `erro:` or `warn:` entry is written to `Console.Error` and the calling component continues its fallback recovery path.

## Failure Cases

- `Console.Error.WriteLine` throws an I/O or ObjectDisposed exception: handled by `UC-002` (swallowed silently).

## Consistency Check

- Unique coverage: Covers formatting and writing internal `erro:` and `warn:` diagnostics to `Console.Error`.
- Closest related use cases: `UC-002` covers swallowing exceptions when `Console.Error` itself fails; `F-202` use cases cover the state transitions that trigger these diagnostics.
- Contradictions: None found.

## Acceptance

- `StderrDiagnostics.Error("relative.log", "file logging disabled: Invalid log path: relative.log")` writes `erro: relative.log` on the first line and six-space-indented `file logging disabled: Invalid log path: relative.log` on the second line to `Console.Error`.
- Diagnostics are written to `Console.Error` even if no console logging provider is registered or the host minimum level is `LogLevel.None`.
- Verified by `JsonFileLoggerProviderTests.AddNyssaJsonFile_WhenTheFileCannotBeOpened_ReportsOnStderrAndKeepsLoggingUsable` and `LoggingTutorials.Keep_Running_When_The_Log_File_Cannot_Be_Opened`.
