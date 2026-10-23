---
feature: F-202
id: UC-003
status: complete
---

# UC-003 Release File Handle Cleanly on Idempotent Provider Disposal

## Actor

Application Host / ServiceProvider Shutdown (`JsonFileLoggerProvider.Dispose` -> `LogFile.Dispose`).

## Goal

Close and release the underlying `FileStream` deterministically under lock when the logging provider or `LogFile` is disposed, safely ignore repeated `Dispose()` calls, and ensure late `WriteRecord` calls after disposal (including when a reopen retry was pending) are ignored without reopening the file or throwing.

## Starting Context

An application host, `ServiceProvider`, `ILoggerFactory`, or test fixture is shutting down and disposing its `JsonFileLoggerProvider` or `LogFile` instance.

## Entry Route

Invoked via `ServiceProvider.Dispose()`, `ILoggerFactory.Dispose()`, `JsonFileLoggerProvider.Dispose()`, or `LogFile.Dispose()`.

## Trigger

Disposal of the owning container, logger factory, provider, or `LogFile`.

## Process

```text
Host or caller disposes the file logging provider
+-- Starting context: JsonFileLoggerProvider / LogFile is active (or awaiting reopen retry)
+-- Entry route: JsonFileLoggerProvider.Dispose() -> LogFile.Dispose()
+-- Trigger: Application shutdown or scope/container disposal
+-- Step 1: Check outer provider disposal state in JsonFileLoggerProvider.Dispose()
|   +-- _disposed is true -> Return immediately
|   \-- _disposed is false -> Set _disposed = true and call _logFile.Dispose()
+-- Step 2: Acquire _lock in LogFile.Dispose() and release stream
|   +-- _disposed is true -> Return immediately
|   \-- _disposed is false
|       +-- Set _disposed = true
|       +-- Dispose _fileStream (if non-null) and set _fileStream = null
|       \-- Release _lock
+-- Step 3: Guard any subsequent WriteRecord call after disposal
|   +-- Acquire _lock in LogFile.WriteRecord
|   \-- _disposed is true -> Return immediately before checking _reopenRetryAt or touching disk
\-- Success: OS file handle is released, previously written records remain intact, and post-dispose calls are no-ops
```

## Step Details

### Step 2: Acquire `_lock` in `LogFile.Dispose()` and release stream
- Synchronizing `_disposed = true` and `_fileStream?.Dispose()` inside `lock (_lock)` ensures an in-flight `WriteRecord` call finishes flushing before the stream is closed, and releases the OS file handle immediately so the file can be read, moved, or deleted on Windows.

### Step 3: Guard any subsequent `WriteRecord` call after disposal
- Checking `if (_disposed) return;` as the very first statement inside `lock (_lock)` in `WriteRecord` guarantees that even if `_fileStream` was `null` with a scheduled `_reopenRetryAt` timestamp from a prior outage (`UC-002`), a post-disposal write will never recreate or reopen the log file on disk.

## Success

The underlying `FileStream` is closed and released, all records written prior to disposal remain flushed on disk, repeated `Dispose()` calls succeed as no-ops, and any `Log` / `WriteRecord` calls after disposal are silently ignored without reopening the file.

## Failure Cases

- None (`Dispose()` and post-disposal `WriteRecord()` are idempotent no-ops).

## Consistency Check

- Unique coverage: Covers synchronized stream disposal, repeated `Dispose()` idempotency, and post-disposal write suppression (including when a reopen retry was pending).
- Closest related use cases: `UC-001` covers startup failure disposal; `UC-002` covers reopen retry when the provider has not been disposed.
- Contradictions: None found.

## Acceptance

- Calling `Dispose()` twice on `LogFile` or `JsonFileLoggerProvider` preserves all previously written records and releases the OS handle so `File.Delete(filePath)` succeeds immediately on Windows.
- Calling `WriteRecord` after `Dispose()` does not throw and does not write the record to the file.
- Calling `WriteRecord` after `Dispose()` when a rotation/reopen failure was pending does not recreate or reopen the file even after the retry clock advances.
- Verified by `LogFileTests.Dispose_CalledTwice_ReleasesTheFileAndKeepsWhatWasWritten`, `LogFileTests.WriteRecord_AfterDispose_IsIgnoredRatherThanThrowing`, and `LogFileTests.WriteRecord_WhenDisposedAfterRotationAndReopenFail_DoesNotReopen`.
