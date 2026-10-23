---
feature: F-001
id: UC-002
status: complete
---

# UC-002 Preserve Existing Shared Directory Permissions

## Actor

File Logging Initialization Pipeline (`LogFile.Initialize` -> `OwnerOnlyAccess.CreateDirectory`).

## Goal

Reuse an existing log directory supplied by the application or operator without stripping its existing Unix mode or Windows inherited ACLs, preventing the library from locking other services out of shared directories like `/var/log` or `C:\ProgramData\Logs`.

## Starting Context

The caller configured a log path inside a directory that already exists on disk (for example, a pre-provisioned shared log directory or a temporary workspace directory created before logger initialization).

## Entry Route

Invoked internally via `OwnerOnlyAccess.CreateDirectory(directoryPath)` during `LogFile.Initialize()`.

## Trigger

`LogFile.Initialize()` prepares the parent directory for `_logPath`.

## Process

```text
LogFile checks an existing target directory
+-- Starting context: directoryPath already exists on disk with operator-assigned permissions
+-- Entry route: LogFile.Initialize calls OwnerOnlyAccess.CreateDirectory(directoryPath)
+-- Trigger: JsonFileLoggerProvider or LogFile initialization
+-- Step 1: Check directory existence
|   \-- Directory.Exists(path) is true -> Return immediately without modifying directory mode or ACL
+-- Step 2: Open and restrict only the log file inside the directory
|   +-- LogFile opens _logPath with OpenAppend
|   \-- LogFile calls OwnerOnlyAccess.ApplyToFile(_logPath) (covered by UC-003)
\-- Success: Existing directory keeps its original permissions while the log file inside it is restricted
```

## Step Details

### Step 1: Check directory existence
- If `Directory.Exists(path)` returns `true`, `OwnerOnlyAccess.CreateDirectory` returns on its first guard clause before calling `Directory.CreateDirectory`, `SetUnixFileMode`, or `ApplyWindowsDirectoryAcl`. This ensures that running with elevated privileges against a shared directory never alters that directory's permissions.

## Success

The pre-existing directory retains its exact Unix file mode or Windows inherited access rules, while the log file created inside it is restricted to the current user.

## Failure Cases

- Existing directory denies write/create access to the current process: `OpenAppend(_logPath)` throws `UnauthorizedAccessException`, caught by `LogFile.Initialize()` and reported on `stderr` without modifying the directory.

## Consistency Check

- Unique coverage: Covers preserving permissions on pre-existing caller or shared directories.
- Closest related use cases: `UC-001` covers creating and restricting a directory that does not yet exist; `UC-003` covers tightening the log file inside the directory.
- Contradictions: None found.

## Acceptance

- On Unix, an existing directory with mode `0755` (`rwxr-xr-x`) retains `0755` after `OwnerOnlyAccess.CreateDirectory` and `LogFile.Initialize()`, while the log file inside it receives `0600`.
- On Windows, an existing directory with inherited permissions retains `AreAccessRulesProtected == false` after `OwnerOnlyAccess.CreateDirectory` and `LogFile.Initialize()`, while the log file inside it receives `AreAccessRulesProtected == true`.
- Verified by `OwnerOnlyAccessTests.CreateDirectory_OnUnixWhenACallerDirectoryAlreadyExists_KeepsItsMode`, `OwnerOnlyAccessTests.CreateDirectory_OnWindowsWhenACallerDirectoryAlreadyExists_KeepsItsInheritedPermissions`, and `LogFileTests.Initialize_OnWindowsWhenACallerDirectoryAlreadyExists_KeepsItsInheritedPermissions`.
