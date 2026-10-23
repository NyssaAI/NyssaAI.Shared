---
feature: F-001
id: UC-003
status: complete
---

# UC-003 Restrict New and Existing Log Files to Current User

## Actor

File Logging Manager (`LogFile.Initialize` and `LogFile.Rotate` -> `LogFile.OpenAppend` and `OwnerOnlyAccess.ApplyToFile`).

## Goal

Ensure that every active JSON log file — whether newly created, reopened from an earlier run with overly permissive rules, or freshly opened after size rotation — is readable and writable only by the current OS user.

## Starting Context

`LogFile` is opening the active log file during `Initialize()` or after archiving the previous file during `Rotate()`. The file may either not exist yet or already exist from a prior run with broader group/world permissions.

## Entry Route

Invoked internally in `LogFile.Initialize()` and `LogFile.Rotate()` via `OpenAppend(_logPath)` followed by `OwnerOnlyAccess.ApplyToFile(_logPath)`.

## Trigger

Opening or rotating the active JSON log file.

## Process

```text
LogFile opens and restricts the active log file
+-- Starting context: parent directory exists; _logPath is ready to be opened for appending
+-- Entry route: LogFile.OpenAppend(_logPath) followed by OwnerOnlyAccess.ApplyToFile(_logPath)
+-- Trigger: LogFile.Initialize() or LogFile.Rotate()
+-- Step 1: Open file stream for appending with creation-time mode on Unix
|   +-- Configure FileStreamOptions (Mode = Append, Access = Write, Share = ReadWrite | Delete)
|   +-- OperatingSystem.IsWindows() is false -> Set options.UnixCreateMode = OwnerReadWrite (0600)
|   \-- Open new FileStream(_logPath, options)
+-- Step 2: Explicitly tighten file permissions via OwnerOnlyAccess.ApplyToFile(_logPath)
|   +-- OperatingSystem.IsWindows() is true
|   |   +-- Create new FileSecurity() with SetAccessRuleProtection(isProtected: true, preserveInheritance: false)
|   |   +-- Add single Allow FullControl rule for WindowsIdentity.GetCurrent().User
|   |   +-- Call FileInfo(_logPath).SetAccessControl(security) to strip inherited and foreign grants
|   |   \-- SetAccessControl throws non-OutOfMemoryException -> Emit StderrDiagnostics.Warning and continue
|   \-- OperatingSystem.IsWindows() is false (Unix / Linux / macOS)
|       +-- Call File.SetUnixFileMode(_logPath, OwnerReadWrite [0600]) to tighten any pre-existing file
|       \-- SetUnixFileMode throws non-OutOfMemoryException -> Emit StderrDiagnostics.Warning and continue
\-- Success: Active log file is open for appending and restricted to the current user only
```

## Step Details

### Step 1: Open file stream for appending with creation-time mode on Unix
- Setting `FileStreamOptions.UnixCreateMode = OwnerOnlyAccess.OwnerReadWrite` (`0600`) closes the race window on Unix where a newly created file would otherwise briefly exist with the process's default `umask` before `chmod` is called.

### Step 2: Explicitly tighten file permissions via `OwnerOnlyAccess.ApplyToFile(_logPath)`
- Because `UnixCreateMode` only applies when `FileStream` creates a brand-new file, `OwnerOnlyAccess.ApplyToFile(_logPath)` is always called immediately afterward to tighten files left behind by earlier runs (e.g., a file previously chmodded to `0644` or granted `WorldSid` read access on Windows) without altering the parent directory's permissions.

## Success

The active log file is open for appending and carries mode `0600` on Unix or a protected DACL granting `FullControl` exclusively to the current Windows user, with all existing file contents preserved.

## Failure Cases

- `File.SetUnixFileMode` or `FileInfo.SetAccessControl` throws (e.g., on a filesystem that does not support POSIX permissions or Windows ACLs): `OwnerOnlyAccess` catches the exception, writes `warn: <path>` via `StderrDiagnostics.Warning`, and leaves the open `FileStream` active so logging continues.

## Consistency Check

- Unique coverage: Covers restricting new and pre-existing log files to the owner at creation, startup reopen, and rotation.
- Closest related use cases: `UC-001` and `UC-002` cover directory-level permissions.
- Contradictions: None found.

## Acceptance

- Newly created log files have mode `0600` on Unix and a protected current-user-only DACL on Windows.
- Pre-existing log files with `GroupRead | OtherRead` on Unix or an explicit `WorldSid` `ReadData` rule on Windows are tightened to owner-only access on `ApplyToFile`, preserving existing file content and leaving parent directory permissions unchanged.
- Verified by `OwnerOnlyAccessTests.Initialize_OnUnix_CreatesTheLogFileReadableOnlyByItsOwner`, `OwnerOnlyAccessTests.ApplyToFile_OnUnixForAFileLeftByAnEarlierRun_TightensIt`, `OwnerOnlyAccessTests.Initialize_OnWindows_LeavesOnlyTheCurrentUserWithAccessToTheLogFile`, and `OwnerOnlyAccessTests.ApplyToFile_ExistingExplicitForeignGrant_RemovesItWithoutChangingDirectoryPermissions`.
