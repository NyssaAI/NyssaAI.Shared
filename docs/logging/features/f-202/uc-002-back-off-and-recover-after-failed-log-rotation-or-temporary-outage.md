---
feature: F-202
id: UC-002
status: complete
---

# UC-002 Back Off and Recover After Failed Log Rotation or Temporary Outage

## Actor

File Logging Writer (`LogFile.WriteRecord` -> `LogFile.Rotate` -> `LogFile.TryReopenAfterFailedRotation`).

## Goal

Continue logging without flooding `stderr` when log rotation fails (by reopening the active file and deferring the next rotation attempt by another trigger size), and automatically recover after a 1-second backoff delay if reopening the active file also temporarily fails.

## Starting Context

`LogFile` is initialized and writing records. Either `_bytesWritten > _rotationTriggerBytes` triggers `Rotate()` and a filesystem conflict prevents moving `<logPath>` to `<logPath>.1`, or `<logPath>` itself becomes temporarily inaccessible during rotation/reopen.

## Entry Route

Invoked internally inside `LogFile.WriteRecord(LogRecord)` under `_lock` when `Rotate()` throws an exception or when `_fileStream is null` on a subsequent write after a failed reopen.

## Trigger

A filesystem exception during `LogFile.Rotate()` or a subsequent `LogFile.WriteRecord` call while `_fileStream is null`.

## Process

```text
LogFile handles a failed rotation and recovers when access returns
+-- Starting context: _bytesWritten > _rotationTriggerBytes triggers Rotate() inside WriteRecord
+-- Entry route: LogFile.Rotate() -> catch block -> TryReopenAfterFailedRotation()
+-- Trigger: Filesystem error during backup shift, archive move, or active file reopen
+-- Step 1: Catch rotation failure and report once
|   +-- Catch Exception (when not OutOfMemoryException) in Rotate()
|   +-- Emit StderrDiagnostics.Error(_logPath, $"failed to rotate log file: {ex.Message}")
|   \-- Call TryReopenAfterFailedRotation()
+-- Step 2: Attempt immediate reopen of _logPath with rotation size backoff
|   +-- OpenAppend(_logPath) succeeds
|   |   +-- Set _bytesWritten = _fileStream.Length
|   |   +-- Set _rotationRetryAtBytes = _bytesWritten + _rotationTriggerBytes (back off by 1 trigger size)
|   |   +-- Set _reopenRetryAt = null
|   |   \-- Write current and subsequent records to reopened _logPath without retrying Rotate() until _bytesWritten >= _rotationRetryAtBytes
|   \-- OpenAppend(_logPath) throws non-OutOfMemoryException (active path temporarily blocked)
|       +-- Set _reopenRetryAt = _timeProvider.GetUtcNow() + ReopenRetryDelay (1 second)
|       +-- Emit StderrDiagnostics.Error(_logPath, $"failed to reopen log file: {ex.Message}")
|       \-- Leave _fileStream = null and drop current record
+-- Step 3: Handle subsequent WriteRecord calls while _fileStream is null
|   +-- _reopenRetryAt is null or _timeProvider.GetUtcNow() < _reopenRetryAt.Value
|   |   \-- Return immediately (drop record silently without spamming stderr)
|   \-- _timeProvider.GetUtcNow() >= _reopenRetryAt.Value
|       +-- Call TryReopenAfterFailedRotation()
|       +-- Still unavailable -> Advance _reopenRetryAt by 1 second, emit one error, and return
|       \-- Access restored -> Reopen _fileStream, clear _reopenRetryAt, and write the incoming record
\-- Success: Logging continues either immediately in the reopened oversized file or automatically once path access returns
```

## Step Details

### Step 2: Attempt immediate reopen of `_logPath` with rotation size backoff
- When archiving `<logPath>` to `<logPath>.1` fails (for example, `<logPath>.1` is locked or occupied by a directory), `<logPath>` itself is still over `_rotationTriggerBytes`. Setting `_rotationRetryAtBytes = _bytesWritten + _rotationTriggerBytes` prevents every subsequent log call from attempting `Rotate()` again and printing duplicate failures to `stderr` until the file grows by another full trigger window (`10 MiB`).

### Step 3: Handle subsequent `WriteRecord` calls while `_fileStream is null`
- When `<logPath>` itself cannot be reopened after `Rotate()` closes the stream, `_reopenRetryAt` throttles reopen attempts to at most once per `ReopenRetryDelay` (`1 second` on `_timeProvider`). Once the obstruction at `_logPath` is cleared and the clock passes `_reopenRetryAt`, the next `WriteRecord` call reopens the stream and resumes writing records.

## Success

Rotation or reopen failures emit a single diagnostic per backoff window, never throw to the caller, keep appending to `<logPath>` when reopen succeeds, and resume writing automatically after temporary outages resolve.

## Failure Cases

- Outage persists across multiple seconds: `TryReopenAfterFailedRotation` logs at most one `failed to reopen log file` diagnostic per elapsed `ReopenRetryDelay` window and drops records written during the outage.
- Provider is disposed while `_reopenRetryAt` is pending: handled by `UC-003` (`_disposed` check inside `_lock` prevents reopening after disposal).

## Consistency Check

- Unique coverage: Covers mid-run rotation failure recovery, `_rotationRetryAtBytes` size backoff, and `_reopenRetryAt` time-based reopen recovery.
- Closest related use cases: `UC-001` covers startup initialization failure; `UC-003` covers disposal after normal operation or failed rotation.
- Contradictions: None found.

## Acceptance

- When `<logPath>.1` is blocked by a directory, `WriteRecord` reports `failed to rotate log file` once, reopens `<logPath>`, writes subsequent records into `<logPath>`, and does not attempt rotation again until `_bytesWritten` grows by another `_rotationTriggerBytes`.
- When both rotation and reopen are blocked, `WriteRecord` reports `failed to reopen log file` once, suppresses retries during the backoff window, and resumes writing records to `<logPath>` once the blockage is removed and the clock advances past `ReopenRetryDelay`.
- Verified by `LogFileTests.WriteRecord_WhenRotationFails_ReportsOnceThenBacksOffByOneTrigger` and `LogFileTests.WriteRecord_WhenRotationAndReopenFail_ResumesAfterAccessIsRestored`.
