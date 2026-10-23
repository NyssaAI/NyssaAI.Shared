---
feature: F-201
id: UC-001
status: complete
---

# UC-001 Rotate Active Log File at Size Threshold and Shift Numbered Backups

## Actor

File Logging Writer (`LogFile.WriteRecord` -> `LogFile.Rotate`).

## Goal

Automatically archive the active JSON log file once its size exceeds the rotation trigger (`10 MiB` by default), shift existing numbered backups (`.1` through `.5`, discarding the oldest when capped at 5), and open a fresh owner-restricted active log file before writing the incoming record.

## Starting Context

`LogFile` has an open `_fileStream` for `_logPath`, and earlier writes have pushed `_bytesWritten` strictly past `_rotationTriggerBytes` (`10,485,760` bytes in production) while `_bytesWritten >= _rotationRetryAtBytes`.

## Entry Route

Invoked internally inside `LogFile.WriteRecord(LogRecord)` under `_lock` before serializing and writing the incoming record.

## Trigger

A new `LogRecord` arrives at `LogFile.WriteRecord` when `_bytesWritten > _rotationTriggerBytes && _bytesWritten >= _rotationRetryAtBytes`.

## Process

```text
LogFile rotates the active file when size exceeds threshold
+-- Starting context: _fileStream is open and _bytesWritten > _rotationTriggerBytes
+-- Entry route: LogFile.WriteRecord checks rotation condition and calls Rotate() under _lock
+-- Trigger: Next WriteRecord call after active file exceeded _rotationTriggerBytes
+-- Step 1: Close and release active FileStream
|   \-- Dispose _fileStream and set _fileStream = null
+-- Step 2: Shift numbered backups from oldest down (index = 4 down to 1)
|   +-- Compute olderPath = "{_logPath}.{index}" and newerPath = "{_logPath}.{index + 1}"
|   +-- olderPath does not exist -> Continue loop
|   \-- olderPath exists
|       +-- newerPath exists -> File.Delete(newerPath)
|       \-- File.Move(olderPath, newerPath)
+-- Step 3: Archive active log file to .1
|   +-- archivePath = "{_logPath}.1"
|   +-- archivePath exists -> File.Delete(archivePath)
|   \-- File.Move(_logPath, archivePath)
+-- Step 4: Prune expired backups and open new active file
|   +-- Call DeleteExpiredBackups() (covered by UC-002)
|   +-- Open new _fileStream = OpenAppend(_logPath)
|   +-- Call OwnerOnlyAccess.ApplyToFile(_logPath) (covered by F-001 / UC-003)
|   \-- Reset _bytesWritten = 0 and _rotationRetryAtBytes = 0
+-- Step 5: Write and flush incoming record to the new active file
|   \-- Serialize record, seek to end, write line + '\n', flush, and set _bytesWritten = _fileStream.Position
\-- Success: Previous log archived to .1 (with older backups shifted up to .5) and new record written to fresh active file
```

## Step Details

### Step 2: Shift numbered backups from oldest down (`index = BackupRetention - 1` down to `1`)
- With `BackupRetention = 5`, the loop runs for `index = 4, 3, 2, 1`, moving `.4 -> .5`, `.3 -> .4`, `.2 -> .3`, and `.1 -> .2`. Any pre-existing `.5` is deleted before `.4` is moved into `.5`, ensuring at most 5 backup files ever exist alongside the active file and `<logPath>.6` is never created.

## Success

The active log file is moved to `<logPath>.1`, older backups shift toward `<logPath>.5` (capped at 5 backups), a new owner-restricted active file is opened at `<logPath>`, and the incoming record is written as the first line of the new active file.

## Failure Cases

- `File.Delete`, `File.Move`, or `OpenAppend` throws during `Rotate()` (e.g., another process or directory blocks `.1`): caught by `Rotate()`, reported via `StderrDiagnostics.Error`, and recovered via `TryReopenAfterFailedRotation()` (covered by `F-202` / `UC-002`).

## Consistency Check

- Unique coverage: Covers size-triggered log rotation (`_bytesWritten > _rotationTriggerBytes`) and numbered backup shifting (`.1`–`.5`).
- Closest related use cases: `UC-002` covers age-based pruning of backups older than 30 days; `F-202` / `UC-002` covers recovery when rotation fails.
- Contradictions: None found.

## Acceptance

- A write that pushes `_bytesWritten` over `_rotationTriggerBytes` completes in the current file; the next write archives the file to `.1` and writes into a new active file.
- Repeated rotations shift `.1 -> .2 -> .3 -> .4 -> .5`, cap the backup count at `5`, and never create `.6`.
- Verified by `LogFileTests.WriteRecord_WhenTheFilePassesTheTrigger_ArchivesItAndStartsANewOne`, `LogFileTests.WriteRecord_WhenRotatingRepeatedly_ShiftsBackupsOldestLast`, `LogFileTests.WriteRecord_AccumulatesBackupsUpToRetention`, and `LogFileTests.WriteRecord_RotatesOnlyOnceTheFilePassesTheTrigger`.
