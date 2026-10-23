---
id: F-201
phase: 2
status: complete
---

# F-201 Size-Based Log Rotation & 30-Day Backup Retention

## Summary

Manages log file growth and disk retention in `LogFile` by rotating the active JSON log file once it exceeds 10 MiB (`RotationTriggerMB = 10`), shifting up to 5 numbered backup files (`.1` through `.5`), and deleting backups older than 30 days (`RetentionDays = 30`) both at initialization and during rotation.

## User Need

Application operators and developers running long-lived services or frequent CLI invocations need local JSON logs to stay bounded in size and automatically purge expired historical backups — even for low-volume applications that rarely cross the size threshold — without requiring an external `logrotate` daemon.

## Use Cases

- [UC-001: Rotate Active Log File at Size Threshold and Shift Numbered Backups](uc-001-rotate-active-log-file-at-size-threshold-and-shift-numbered-backups.md)
- [UC-002: Prune Expired Backups Older Than 30 Days at Startup and Rotation](uc-002-prune-expired-backups-older-than-30-days-at-startup-and-rotation.md)

## Scope

- Tracking active file size (`_bytesWritten`) from initial `FileStream.Length` and post-write `FileStream.Position`.
- Triggering `Rotate()` inside `WriteRecord` before writing the next record whenever `_bytesWritten > _rotationTriggerBytes` (default `10 * 1024 * 1024` = `10,485,760` bytes) and `_bytesWritten >= _rotationRetryAtBytes`.
- Closing the active stream, shifting existing numbered backups from oldest down (`<logPath>.4` -> `<logPath>.5` down to `<logPath>.1` -> `<logPath>.2`, capped at `BackupRetention = 5`), moving the active `<logPath>` to `<logPath>.1`, and opening a new owner-restricted active file at `<logPath>`.
- Pruning expired backups (`DeleteExpiredBackups`) both during `LogFile.Initialize()` and during `Rotate()` by walking backup indices `1..5` explicitly (avoiding Windows globbing pitfalls where `name.log.*` matches `name.log`) and deleting any backup whose `LastWriteTimeUtc < clock.GetUtcNow().UtcDateTime.AddDays(-30)`.
- Reporting individual backup deletion failures via `StderrDiagnostics.Warning` and continuing with remaining backups.

## Out of Scope

- Time-based (midnight) rotation of an already-open `WithRotatingJsonLog` provider (`yyyy.MM.dd-` is chosen when the provider opens and rotated by size `*.1`–`*.5`).
- Deleting older date-prefixed base files (`yyyy.MM.dd-application-log.json`) from earlier process runs (only `.1`–`.5` backups of the opened path are pruned).
- Rejecting or splitting a single record larger than 10 MiB before it is written (the record that pushes `_bytesWritten` over the threshold completes, and the subsequent write triggers rotation).

## Inputs

- Active `_logPath` (`string`), `_rotationTriggerBytes` (`long`, default 10 MiB, internal constructor override for testing), and `TimeProvider`.
- Existing `<logPath>.1` through `<logPath>.5` backup files and their `LastWriteTimeUtc` metadata on disk.

## Outputs

- Rotated backup files `<logPath>.1` (newest) through `<logPath>.5` (oldest) on disk.
- Freshly opened and owner-restricted `<logPath>` stream with `_bytesWritten = 0` and `_rotationRetryAtBytes = 0`.
- Deleted expired backup files older than 30 days, or `StderrDiagnostics.Warning` lines on `stderr` if deletion of an expired backup fails.

## Acceptance

- Writing a record when `_bytesWritten > _rotationTriggerBytes` shifts existing `.1`..`.4` backups to `.2`..`.5`, moves `<logPath>` to `<logPath>.1`, and writes the new record into a fresh `<logPath>`.
- Repeated rotations cap backup files at `BackupRetention` (`5`) and never create `<logPath>.6`.
- `LogFile.Initialize()` and `Rotate()` delete any `<logPath>.1`..`<logPath>.5` file whose `LastWriteTimeUtc` is older than `GetUtcNow().UtcDateTime.AddDays(-30)` while keeping backups within the 30-day window.
- Walking backup indices `1..5` never deletes the active `<logPath>` on Windows.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- `F-001` (`OwnerOnlyAccess.ApplyToFile` on newly opened active file after rotation).
- `F-002` (`StderrDiagnostics` for backup deletion warnings and rotation errors).
- `F-102` (`LogFile.WriteRecord` byte tracking and stream management).
