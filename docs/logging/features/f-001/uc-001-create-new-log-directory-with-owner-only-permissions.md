---
feature: F-001
id: UC-001
status: complete
---

# UC-001 Create New Log Directory with Owner-Only Permissions

## Actor

File Logging Initialization Pipeline (`LogFile.Initialize` -> `OwnerOnlyAccess.CreateDirectory`).

## Goal

Create a missing log directory on disk and immediately restrict its access permissions to the OS account running the current process so other local users cannot list or tamper with the directory's log files.

## Starting Context

An application has registered a file logging destination (`WithRotatingJsonLog`, `AddNyssaJsonFile`, or `AddNyssaLogging(filePath)`), and the parent directory of the resolved `_logPath` does not yet exist on disk.

## Entry Route

Invoked internally by `LogFile.Initialize()` via `OwnerOnlyAccess.CreateDirectory(directoryPath)` when `JsonFileLoggerProvider.TryCreate` initializes the file sink.

## Trigger

The `ILoggerProvider` factory resolves the `JsonFileLoggerProvider` singleton and calls `LogFile.Initialize()`.

## Process

```text
LogFile initializes a missing target directory
+-- Starting context: _logPath has a non-empty directoryPath that does not exist on disk
+-- Entry route: LogFile.Initialize calls OwnerOnlyAccess.CreateDirectory(directoryPath)
+-- Trigger: JsonFileLoggerProvider.TryCreate invoked during logger provider resolution
+-- Step 1: Check whether directory already exists
|   +-- Directory.Exists(path) is true -> Return immediately (covered by UC-002)
|   \-- Directory.Exists(path) is false -> Proceed to platform creation
+-- Step 2: Create directory and apply owner-only access rules
|   +-- OperatingSystem.IsWindows() is true
|   |   +-- Call Directory.CreateDirectory(path)
|   |   +-- Call ApplyWindowsDirectoryAcl(path)
|   |   |   +-- Build DirectorySecurity with SetAccessRuleProtection(isProtected: true, preserveInheritance: false)
|   |   |   +-- Resolve current user SID via WindowsIdentity.GetCurrent().User
|   |   |   +-- Add inheritable FullControl Allow rule (ContainerInherit | ObjectInherit) for current user
|   |   |   +-- Apply ACL via DirectoryInfo(path).SetAccessControl(security)
|   |   |   \-- ACL update throws non-OutOfMemoryException -> Emit StderrDiagnostics.Warning and continue
|   \-- OperatingSystem.IsWindows() is false (Unix / Linux / macOS)
|       +-- Call Directory.CreateDirectory(path, OwnerReadWriteExecute [0700])
|       +-- Call RestrictUnixPath(path, OwnerReadWriteExecute [0700]) via File.SetUnixFileMode
|       \-- SetUnixFileMode throws non-OutOfMemoryException -> Emit StderrDiagnostics.Warning and continue
\-- Success: Directory exists on disk restricted to the current user
```

## Step Details

### Step 2: Create directory and apply owner-only access rules
- **Windows Details:** `ApplyWindowsDirectoryAcl` constructs a fresh `DirectorySecurity` rather than modifying the inherited security descriptor, calls `SetAccessRuleProtection(isProtected: true, preserveInheritance: false)` to strip inherited permissions, and adds a single `FileSystemAccessRule` for `WindowsIdentity.GetCurrent().User` with `FileSystemRights.FullControl` and `InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit`.
- **Unix Details:** `Directory.CreateDirectory(path, OwnerReadWriteExecute)` passes mode `0700` (`UserRead | UserWrite | UserExecute`) at creation time, and `RestrictUnixPath` subsequently calls `File.SetUnixFileMode(path, OwnerReadWriteExecute)` to ensure the mode is not weakened by the process `umask`.
- **Best-Effort Failure Boundary:** Any exception other than `OutOfMemoryException` thrown during ACL/mode application is caught and logged via `StderrDiagnostics.Warning(path, ...)` so directory permission quirks on specialized mounts do not abort logging.

## Success

The missing directory is created on disk and restricted exclusively to the current user (`0700` on Unix or protected current-user inheritable `FullControl` DACL on Windows).

## Failure Cases

- Parent path cannot be created (e.g., read-only filesystem or invalid volume): `Directory.CreateDirectory` throws, which is caught by `LogFile.Initialize()` and disables the file destination (see `F-202` / `UC-001`).
- ACL or `UnixFileMode` modification fails after directory creation: `OwnerOnlyAccess` emits `warn: <path>` via `StderrDiagnostics.Warning` and continues initialization.

## Consistency Check

- Unique coverage: Covers creating a new log directory and applying owner-only directory permissions across Windows and Unix.
- Closest related use cases: `UC-002` covers the branch where the directory already exists on disk; `UC-003` covers restricting the log file itself.
- Contradictions: None found.

## Acceptance

- On Unix, a newly created directory has `File.GetUnixFileMode(directory) == OwnerOnlyAccess.OwnerReadWriteExecute` (`0700`).
- On Windows, a newly created directory has `AreAccessRulesProtected == true` and exactly one `FileSystemAccessRule` granting `FullControl` to `WindowsIdentity.GetCurrent().User`.
- Verified by `OwnerOnlyAccessTests.CreateDirectory_OnUnix_RestrictsTheDirectoryToItsOwner` and `OwnerOnlyAccessTests.CreateDirectory_OnWindows_LeavesOnlyTheCurrentUserWithAccess`.
