---
feature: F-001
id: UC-001
status: planned
---

# UC-001 Verify Directory and File Write Safety

## Actor

Configuration Bootstrap / Document Factory (`ConfigurationManifestReader`, `ConfigurationManifestWriter`, `ConfigurationDocumentFactoryLocalFile`).

## Goal

Ensure that an existing target configuration directory or manifest file is owned by a trusted identity and cannot be modified or re-permissioned by other accounts on the machine before any configuration data is read or written.

## Starting Context

The application is bootstrapping or opening a namespace document. A path to a configuration directory (e.g., `<configurationDirectory>`) or a manifest file (`~/.nyssa-ai/config-manifest.json`) has been identified.

## Entry Route

Invoked internally via `DirectorySafety.Verify(directory)` or `DirectorySafety.VerifyFile(path)` during client initialization or when creating a document handle.

## Trigger

A request to read or write a manifest file or to instantiate a `ConfigurationDocumentLocalFile` within a designated configuration directory.

## Process

```text
Actor verifies write safety of path
+-- Starting context: path to directory or file supplied
+-- Entry route: DirectorySafety.Verify or VerifyFile called
+-- Trigger: startup bootstrap or namespace document factory invocation
+-- Step 1: Check existence of target path
|   +-- Path is blank/whitespace or does not exist on disk
|   |   \-- Return Result.Success (first-use creation will establish ownership)
|   \-- Path exists -> Proceed to platform permission check
+-- Step 2: Inspect platform security descriptors
|   +-- OS is Windows
|   |   +-- Query FileSystemSecurity owner and DACL rules
|   |   +-- Owner check: verify owner is current user, LocalSystem, or BuiltinAdministrators
|   |   |   \-- Owner not trusted -> Fail with Result.Failure(NotOwned) [Forbidden]
|   |   +-- Access rule check: inspect Allow rules granting write/append/take-ownership/change-permissions
|   |   |   +-- Multi-user identity (Everyone, Authenticated Users, BuiltinUsers, Guests) granted write
|   |   |   |   \-- Fail with Result.Failure(Writable) [Forbidden]
|   |   |   \-- Deny rules present or only trusted identities granted write -> Safe
|   \-- OS is Unix / POSIX
|       +-- Read UnixFileMode via file status
|       +-- Mode check: inspect GroupWrite (g+w) and OtherWrite (o+w)
|       |   +-- Either bit set -> Fail with Result.Failure(Writable) [Forbidden]
|       |   \-- Neither bit set -> Safe
\-- Success: Return Result.Success indicating path is safe to use
```

## Step Details

### Step 2: Inspect platform security descriptors
- **Windows Details:** The inspection checks `FileSystemRights.WriteData`, `AppendData`, `ChangePermissions`, and `TakeOwnership`. Accounts belonging to `WellKnownSidType.BuiltinAdministratorsSid` or `LocalSystemSid` are permitted owners because administrative accounts can assume ownership unconditionally. Well-known multi-user identities (`WorldSid`, `AuthenticatedUserSid`, `BuiltinUsersSid`, `BuiltinGuestsSid`) are forbidden from having `Allow` write rules. `Deny` rules for these identities do not trigger a violation.
- **Unix Details:** Evaluates mode bits for `UnixFileMode.GroupWrite` and `UnixFileMode.OtherWrite`. (Ownership `uid` checking is omitted due to portability constraints of glibc `stat` symbols across architectures).
- **Error Boundary:** Any `IOException`, `UnauthorizedAccessException`, `PlatformNotSupportedException`, or `PrivilegeNotHeldException` thrown during ACL interrogation is caught and converted to `DirectorySafety.NotVerifiable` (`ErrorCategory.Forbidden`).

## Success

The path is confirmed to be private and safe from tampering by other local accounts, and `Result.Success` is returned to the caller.

## Failure Cases

- `NotOwned`: Directory or file is owned by an untrusted account (Windows).
- `Writable`: Directory or file grants write, append, or ACL alteration permissions to multi-user identities (Windows) or group/other (Unix).
- `NotVerifiable`: The filesystem or security subsystem threw an error while querying access controls; the system fails closed with `ErrorCategory.Forbidden`.

## Consistency Check

- Unique coverage: Covers write and ownership safety verification of directories and general configuration files.
- Closest related use cases: `UC-002` covers stricter read-permission checks on private files holding plaintext bootstrap secrets; `UC-003` covers the actual creation of safe directories when absent.
- Contradictions: None found.

## Acceptance

- Absent paths and empty/whitespace paths return `Result.Success`.
- Directories with `Everyone` (Windows) or `o+w` / `g+w` (Unix) write access fail with `ErrorCategory.Forbidden`.
- Windows `Deny` rules on multi-user groups do not trigger false positives.
- Platform inspection exceptions fail closed with actionable error messages without crashing.
