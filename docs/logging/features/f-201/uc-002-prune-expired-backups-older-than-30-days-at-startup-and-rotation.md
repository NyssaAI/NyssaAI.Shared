---
feature: F-201
id: UC-002
status: complete
---

# UC-002 Prune Expired Backups Older Than 30 Days at Startup and Rotation

## Actor

File Logging Retention Cleaner (`LogFile.Initialize` and `LogFile.Rotate` -> `LogFile.DeleteExpiredBackups`).

## Goal

Remove numbered backup files (`<logPath>.1` through `<logPath>.5`) whose last-write timestamp is older than 30 days (`RetentionDays = 30`), executing both at startup initialization (so low-volume services still enforce age retention) and during size rotation, without ever matching or deleting the active log file on Windows.

## Starting Context

`LogFile` holds `_lock` during `Initialize()` or `Rotate()`, and one or more numbered backup files (`<logPath>.1`..`<logPath>.5`) may exist in the log directory.

## Entry Route

Invoked internally via `DeleteExpiredBackups()` inside `LogFile.Initialize()` and `LogFile.Rotate()`.

## Trigger

Startup initialization of `LogFile` or completion of backup shifting in `LogFile.Rotate()`.

## Process

```text
LogFile prunes rotated backups older than 30 days
+-- Starting context: _lock is held during Initialize() or Rotate(); TimeProvider is available
+-- Entry route: LogFile.DeleteExpiredBackups()
+-- Trigger: LogFile.Initialize() or LogFile.Rotate()
+-- Step 1: Compute 30-day UTC retention cutoff
|   \-- cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-RetentionDays) [RetentionDays = 30]
+-- Step 2: Walk explicit backup indices 1 through BackupRetention (5)
|   +-- Construct candidate path = "{_logPath}.{index}"
|   +-- !File.Exists(path) -> Continue to next index
|   +-- new FileInfo(path).LastWriteTimeUtc >= cutoff -> Keep fresh backup and continue
|   \-- LastWriteTimeUtc < cutoff -> Delete expired backup via File.Delete(path)
|       \-- File.Delete or FileInfo throws non-OutOfMemoryException
|           \-- Emit StderrDiagnostics.Warning(path, "failed to delete expired log backup: ...") and continue loop
\-- Success: All accessible backups older than 30 days are removed while fresh backups and the active file remain intact
```

## Step Details

### Step 2: Walk explicit backup indices `1` through `BackupRetention` (`5`)
- Rather than calling `Directory.GetFiles(dir, "name.log.*")`, `DeleteExpiredBackups` explicitly iterates `index = 1..5`. On Windows, Win32 wildcard matching treats `name.log.*` as matching `name.log` (zero-length extension), which would otherwise risk selecting and deleting the active log file itself.

## Success

Every numbered backup `<logPath>.1`..`<logPath>.5` with `LastWriteTimeUtc < cutoff` is deleted from disk, while backups with `LastWriteTimeUtc >= cutoff` and the active `<logPath>` are preserved.

## Failure Cases

- An expired backup file is locked or cannot be deleted: `DeleteExpiredBackups` catches the exception (`when (ex is not OutOfMemoryException)`), emits `StderrDiagnostics.Warning(path, $"failed to delete expired log backup: {ex.Message}")`, and continues checking the remaining backup indices.

## Consistency Check

- Unique coverage: Covers 30-day age cutoff calculation via `TimeProvider`, startup + rotation execution, explicit `1..5` index iteration, and per-backup warning tolerance.
- Closest related use cases: `UC-001` covers size-based rotation and backup count capping (`.1`–`.5`).
- Contradictions: None found.

## Acceptance

- Backups with `LastWriteTimeUtc` more than 30 days before `_timeProvider.GetUtcNow()` are deleted during `LogFile.Initialize()`, while backups within 30 days are preserved.
- Advancing an injected `TimeProvider` past 30 days and re-initializing `LogFile` deletes previously created backups without mutating filesystem timestamps.
- Verified by `LogFileTests.Initialize_WithAnExpiredBackup_DeletesItAndKeepsAFreshOne` and `LogFileTests.Initialize_WhenTheClockPassesTheRetentionWindow_DeletesTheBackup`.
