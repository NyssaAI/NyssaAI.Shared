---
id: F-202
phase: 2
status: complete
---

# F-202 Fault-Tolerant File Initialization, Rotation Recovery & Disposal

## Summary

Ensures that filesystem failures never crash host startup or runtime logging calls by falling back to `NullLoggerProvider.Instance` when a log file cannot be opened, backing off rotation/reopen retries when mid-run rotation or file access fails, and releasing file streams cleanly on idempotent disposal.

## User Need

Application and CLI engineers need a hard guarantee that a misconfigured path (such as a bare filename passed to `AddNyssaJsonFile`), a read-only volume, a locked backup file during rotation, or a transient storage outage will never throw unhandled exceptions into application business logic or spam `stderr` on every log call.

## Use Cases

- [UC-001: Degrade to Null Logger Provider When Log File Cannot Be Opened](uc-001-degrade-to-null-logger-provider-when-log-file-cannot-be-opened.md)
- [UC-002: Back Off and Recover After Failed Log Rotation or Temporary Outage](uc-002-back-off-and-recover-after-failed-log-rotation-or-temporary-outage.md)
- [UC-003: Release File Handle Cleanly on Idempotent Provider Disposal](uc-003-release-file-handle-cleanly-on-idempotent-provider-disposal.md)

## Scope

- Validating `filePath` in `LogFile.Initialize()` (rejecting paths with no directory component, e.g., `"relative.log"`, or catching filesystem exceptions) and returning an error string from `JsonFileLoggerProvider.TryCreate`.
- Emitting `StderrDiagnostics.Error(destination, $"file logging disabled: {error}")` and returning `NullLoggerProvider.Instance` from the `ILoggerProvider` factory in `LoggingBuilderExtensions` when `TryCreate` returns `null`, leaving all other configured logging providers operational.
- Catching non-`OutOfMemoryException` exceptions during `LogFile.Rotate()`, emitting `StderrDiagnostics.Error(_logPath, $"failed to rotate log file: {ex.Message}")`, and invoking `TryReopenAfterFailedRotation()`.
- Reopening `<logPath>` for appending in `TryReopenAfterFailedRotation()` and setting `_rotationRetryAtBytes = _bytesWritten + _rotationTriggerBytes` so subsequent writes continue appending without re-attempting rotation until the file grows by another trigger window (`10 MiB`).
- Scheduling `_reopenRetryAt = _timeProvider.GetUtcNow() + ReopenRetryDelay` (1 second) and emitting `StderrDiagnostics.Error(_logPath, $"failed to reopen log file: {ex.Message}")` when reopening `<logPath>` also fails, silently dropping records during the backoff window and automatically resuming once access is restored.
- Guarding `LogFile.Dispose()` and `JsonFileLoggerProvider.Dispose()` under `_lock` with `_disposed` so repeated `Dispose()` calls are safe and writes after disposal (including after a failed rotation/reopen) never reopen the file or throw.

## Out of Scope

- In-memory buffering or replay of log records dropped while the file stream cannot be opened or reopened.
- Catching `OutOfMemoryException` (`OutOfMemoryException` is never caught by `LogFile` or `JsonFileLoggerProvider`).

## Inputs

- Target file path (`string`), `TimeProvider` clock, and runtime filesystem state (directory existence, permissions, contending locks/directories blocking `.1` or `<logPath>`).
- `Dispose()` lifecycle calls from `ServiceProvider` / `ILoggerFactory` or direct `LogFile` consumers.

## Outputs

- `NullLoggerProvider.Instance` registration and `erro: <path>\n      file logging disabled: ...` on `stderr` when startup initialization fails.
- Reopened `FileStream` with `_rotationRetryAtBytes` backoff threshold, or `_reopenRetryAt` 1-second timer when reopen fails.
- Closed and released `FileStream` handle on `Dispose()`.

## Acceptance

- Supplying an unwritable path or a bare filename (`"relative.log"`) to `AddNyssaJsonFile` writes `file logging disabled` to `stderr`, leaves the application running, and delivers logs normally to all other configured destinations.
- When `Rotate()` fails (e.g., `<logPath>.1` is blocked by a directory), `LogFile` reports the rotation failure once on `stderr`, reopens `<logPath>`, continues writing subsequent records, and does not retry rotation until another `_rotationTriggerBytes` bytes have been written.
- When both `Rotate()` and `TryReopenAfterFailedRotation()` fail, `LogFile` reports the reopen failure once, suppresses retries for 1 second (`ReopenRetryDelay`), and resumes writing to `<logPath>` once access is restored after the retry delay.
- Calling `WriteRecord` after `Dispose()` — including when a reopen retry was pending — is ignored without reopening the file or throwing, and calling `Dispose()` multiple times releases the OS file lock so the file can be deleted immediately on Windows.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- `F-002` (`StderrDiagnostics` for initialization, rotation, and reopen error reporting).
- `F-102` (`JsonFileLoggerProvider` and `LogFile` write pipeline).
- `F-201` (`LogFile.Rotate` lifecycle).
