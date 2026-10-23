---
feature: F-001
id: UC-003
status: planned
---

# UC-003 Provision Directory and Atomically Publish Files

## Actor

Manifest Writer / Namespace Document Store (`ProtectedFileSystem`, `ConfigurationManifestWriter`, `ConfigurationDocumentLocalFile`).

## Goal

Create new configuration directories with strict owner-only permissions and safely publish updated configuration files via temporary files so that readers never observe partially written content and volume inheritance cannot grant multi-user access.

## Starting Context

An application needs to initialize its configuration folder (e.g., `<configurationDirectory>`), or write updated JSON content to a manifest or namespace document file (`<targetPath>`).

## Entry Route

Called via `ProtectedFileSystem.CreateDirectory(directory)` or `ProtectedFileSystem.WriteAllText(temporary, content)` / `ProtectedFileSystem.WriteAllTextAsync(...)` followed by an atomic filesystem move.

## Trigger

An application's section is provisioned in the manifest, or a configuration document mutation completes and requires disk publication.

## Process

```text
Actor creates protected directory or publishes file
+-- Starting context: target directory path or file path + JSON content
+-- Entry route: ProtectedFileSystem directory or atomic publish invocation
+-- Trigger: initial setup or namespace document save
+-- Phase A: Directory Provisioning (if directory does not exist)
|   +-- Check Directory.Exists(fullPath)
|   |   +-- Directory already exists -> Return Result.Success (preserves existing permissions)
|   |   \-- Directory does not exist -> Create with restricted permissions
|   +-- OS is Windows
|   |   +-- Build DirectorySecurity descriptor:
|   |   |   +-- SetAccessRuleProtection(isProtected: true, preserveInheritance: false)
|   |   |   +-- Add FullControl rule for current user
|   |   |   +-- Add FullControl rule for LocalSystem
|   |   |   \-- Add FullControl rule for BuiltinAdministrators
|   |   \-- Create directory applying DirectorySecurity
|   \-- OS is Unix / POSIX
|       \-- Create directory with mode 0700 (UserRead | UserWrite | UserExecute)
+-- Phase B: Atomic File Publication
|   +-- Ensure parent directory exists via ProtectedFileSystem.CreateDirectory
|   +-- Generate unique temporary path: <targetPath>.<guid>.tmp
|   +-- Write serialized JSON to temporary file
|   |   +-- UTF-8 encoding without BOM
|   |   +-- OS is Windows: FileOptions / FileShare.None (inherits parent directory ACLs)
|   |   +-- OS is Unix: FileOptions with UnixCreateMode 0600 (UserRead | UserWrite)
|   |   \-- Write fails -> Catch error, cleanup temporary file, fail with Result.Failure(FileNotWritable)
|   +-- Atomic Rename / Replace: File.Move(temporary, targetPath, overwrite: true)
|   |   \-- Move fails -> Catch error, fail with Result.Failure(FileNotWritable)
|   \-- Cleanup in finally block: delete temporary file if it still exists
\-- Success: Target directory created or file atomically replaced with restricted permissions
```

## Step Details

### Phase A: Directory Provisioning
- **Inheritance Blocking on Windows:** Default NTFS volumes grant `Authenticated Users` modify rights to subdirectories. `CreateWindows` explicitly sets `isProtected: true` and `preserveInheritance: false` on the DACL so volume-level inheritance is severed at the configuration folder root. Only the current user SID, `LocalSystemSid`, and `BuiltinAdministratorsSid` receive ACEs.
- **Unix Permissions:** Directories are created with octal `0700`.
- **Existing Directories:** If the directory already exists, `CreateDirectory` does not alter its existing ACLs. Validation is delegated to `DirectorySafety.Verify` (`UC-001`).

### Phase B: Atomic File Publication
- **Temporary File Creation:** The temporary file is created in the exact same directory as the target file (`<path>.<guid>.tmp`) to ensure the rename is an intra-filesystem metadata update rather than a cross-device copy.
- **Atomicity & Cleanup:** `File.Move(temporary, targetPath, overwrite: true)` replaces the destination atomically. A `finally` block guarantees `File.Delete(temporary)` is executed if an exception occurs during serialization or write, preventing orphaned temporary files.

## Success

The directory is established with restrictive permissions, or the target file is updated atomically without any period where readers could observe a truncated or half-written document.

## Failure Cases

- `DirectoryNotCreatable`: Insufficient filesystem privileges or invalid path preventing directory creation (`ErrorCategory.Forbidden` or `Unexpected`).
- `FileNotWritable`: IO failure or permissions issue writing the temporary file or replacing the target destination.

## Consistency Check

- Unique coverage: Covers filesystem write actions (directory creation, file writing, atomic replacement).
- Closest related use cases: `UC-001` and `UC-002` only read and verify permissions; `UC-004` manages the lock file guarding concurrent executions of this write process.
- Contradictions: None found.

## Acceptance

- Newly created directories on Windows have inheritance disabled and multi-user groups stripped.
- Newly created directories on POSIX have mode `0700`.
- Temporary files use mode `0600` on POSIX and are deleted even if writing fails mid-stream.
- Destination files never contain partial JSON or invalid byte sequences during writes.
