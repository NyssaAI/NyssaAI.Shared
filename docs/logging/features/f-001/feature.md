---
id: F-001
phase: 0
status: complete
---

# F-001 Owner-Only File & Directory Access Control

## Summary

Restricts newly created log directories (`0700` on Unix, protected current-user inheritable DACL on Windows) and new or pre-existing log files (`0600` on Unix, protected current-user DACL on Windows) to the current OS account via `OwnerOnlyAccess`, while leaving permissions on pre-existing caller-supplied directories unchanged.

## User Need

Application engineers and system operators need local JSON log files — which may contain exception details, stack traces, and structured application fields — restricted to the OS account running the process, without accidentally locking other services out of shared system log directories such as `/var/log` or `C:\ProgramData\Logs`.

## Use Cases

- [UC-001: Create New Log Directory with Owner-Only Permissions](uc-001-create-new-log-directory-with-owner-only-permissions.md)
- [UC-002: Preserve Existing Shared Directory Permissions](uc-002-preserve-existing-shared-directory-permissions.md)
- [UC-003: Restrict New and Existing Log Files to Current User](uc-003-restrict-new-and-existing-log-files-to-current-user.md)

## Scope

- Creating missing log directories restricted to the current OS account (`UnixFileMode.UserRead | UserWrite | UserExecute` `0700` on Unix; protected `DirectorySecurity` with `ContainerInherit | ObjectInherit` `FullControl` for `WindowsIdentity.GetCurrent().User` on Windows).
- Preserving permissions on any directory that already exists prior to `OwnerOnlyAccess.CreateDirectory(path)`.
- Creating new log files with `UnixCreateMode = 0600` (`OwnerReadWrite`) in `LogFile.OpenAppend` on Unix so no umask window exposes newly created files.
- Tightening new and pre-existing log files via `OwnerOnlyAccess.ApplyToFile(path)` (`0600` on Unix; protected `FileSecurity` replacing inherited and explicit foreign rules with a single current-user `FullControl` rule on Windows) on initialization and after rotation.
- Reporting permission modification failures to standard error via `StderrDiagnostics.Warning` as best-effort warnings without throwing or disabling logging.

## Out of Scope

- Failing closed or aborting file logging when a filesystem does not support POSIX modes or Windows ACLs (permission restriction in `NyssaAI.Logging` is best-effort).
- Encrypting log files at rest.
- Modifying permissions on pre-existing parent or shared directories.

## Inputs

- Target directory path (`string`) passed to `OwnerOnlyAccess.CreateDirectory`.
- Target log file path (`string`) passed to `LogFile.OpenAppend` and `OwnerOnlyAccess.ApplyToFile`.
- Current OS platform (`OperatingSystem.IsWindows()`) and current Windows user SID (`WindowsIdentity.GetCurrent().User`).

## Outputs

- Newly created directory with mode `0700` (Unix) or protected current-user inheritable `FullControl` DACL (Windows).
- Newly created or reopened active log file with mode `0600` (Unix) or protected current-user `FullControl` DACL (Windows).
- Best-effort warning diagnostics on `stderr` (`warn: <path>`) if `File.SetUnixFileMode` or `SetAccessControl` throws a non-`OutOfMemoryException` error.

## Acceptance

- Creating a new directory via `OwnerOnlyAccess.CreateDirectory` results in `0700` on Unix and a protected DACL with a single `Allow` `FullControl` rule for `WindowsIdentity.GetCurrent().User` on Windows.
- Calling `OwnerOnlyAccess.CreateDirectory` on an already-existing directory leaves its Unix mode (`0755`) or Windows inherited DACL (`AreAccessRulesProtected == false`) untouched.
- Opening a new or pre-existing log file tightens the file itself to `0600` on Unix or current-user-only protected DACL on Windows (removing foreign grants such as `WorldSid`) without changing the parent directory's ACL.
- Permission failures emit a `warn:` diagnostic on `stderr` and never throw to the caller.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- `F-002` (`StderrDiagnostics` for reporting best-effort permission warnings).
