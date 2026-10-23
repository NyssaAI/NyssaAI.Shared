---
feature: F-001
id: UC-002
status: planned
---

# UC-002 Verify Private Manifest Permissions

## Actor

Manifest Reader (`ConfigurationManifestReader`).

## Goal

Verify that a configuration manifest containing sensitive bootstrap secrets (such as `keyVault.secrets`) is completely private—meaning other accounts on the machine cannot read or execute it—and provide an exact remediation command if exposed.

## Starting Context

A manifest file exists at `~/.nyssa-ai/config-manifest.json` (or a custom path). The manifest has been parsed or is about to be used, and contains at least one application entry with a non-empty `keyVault.secrets` collection.

## Entry Route

Invoked via `DirectorySafety.VerifyPrivateFile(manifestPath)` before consuming secrets from the manifest.

## Trigger

An application section in the manifest declares remote vault authentication settings containing static secrets (`keyVault.secrets`).

## Process

```text
Actor verifies private file read permissions
+-- Starting context: manifest file path identified with keyVault.secrets
+-- Entry route: DirectorySafety.VerifyPrivateFile(path) called
+-- Trigger: manifest contains vault secrets requiring read lockdown
+-- Step 1: Execute baseline write safety verification (UC-001)
|   +-- Call DirectorySafety.VerifyFile(path)
|   +-- Verification fails (untrusted owner or writable by others)
|   |   \-- Fail immediately with Result.Failure(writableError)
|   \-- File is writable only by owner -> Proceed to read checks
+-- Step 2: Check existence of target file
|   +-- Path is blank or does not exist
|   |   \-- Return Result.Success (nothing exposed yet)
|   \-- Path exists -> Check read access rules
+-- Step 3: Inspect platform read security descriptors
|   +-- OS is Windows
|   |   +-- Query DACL rules for FileSystemRights.ReadData
|   |   +-- Check for Allow rules granted to multi-user identities (Everyone, Authenticated Users, BuiltinUsers, BuiltinGuests)
|   |   |   +-- Multi-user identity has ReadData access
|   |   |   |   \-- Fail with Result.Failure(Exposed) [Forbidden]
|   |   |   |       \-- Error provides fix: icacls "<path>" /inheritance:r /grant:r "%USERNAME%:F"
|   |   |   \-- No multi-user read rules -> Safe
|   \-- OS is Unix / POSIX
|       +-- Read UnixFileMode via file status
|       +-- Check for OtherRead/OtherExecute (o+r, o+x) or GroupRead/GroupExecute (g+r, g+x)
|       |   +-- Other read/execute bit set
|       |   |   \-- Fail with Result.Failure(Exposed) [Forbidden]
|       |   |       \-- Error provides fix: chmod 600 '<path>'
|       |   +-- Group read/execute bit set
|       |   |   \-- Fail with Result.Failure(Exposed) [Forbidden]
|       |   |       \-- Error provides fix: chmod 600 '<path>'
|       |   \-- Only UserRead/UserWrite bits set (0600) -> Safe
\-- Success: Return Result.Success confirming file is strictly private
```

## Step Details

### Step 3: Inspect platform read security descriptors
- **Windows Details:** Scans all access rules for `AccessControlType.Allow` where `(rule.FileSystemRights & FileSystemRights.ReadData) != 0`. If `rule.IdentityReference` matches `WorldSid`, `AuthenticatedUserSid`, `BuiltinUsersSid`, or `BuiltinGuestsSid`, the check fails immediately. The technical error message details the offending group and includes the command: `icacls "<path>" /inheritance:r /grant:r "%USERNAME%:F"`.
- **Unix Details:** Evaluates `mode & (UnixFileMode.OtherRead | UnixFileMode.OtherExecute)` and `mode & (UnixFileMode.GroupRead | UnixFileMode.GroupExecute)`. If any group or other bit is set, it fails immediately with `ErrorCategory.Forbidden`. The technical error message specifies which bits leaked access and instructs the operator to run: `chmod 600 '<path>'`.
- **Secret Safety:** The error message never prints or references the contents of `keyVault.secrets`, only the path, offending permissions, and remediation command.

## Success

The manifest file is verified to be non-readable by any unauthorized account on the machine, preventing secret exposure before startup completes.

## Failure Cases

- `Writable` / `NotOwned`: Fails write-safety prerequisites inherited from `UC-001`.
- `Exposed`: File has read permissions granted to multi-user groups (Windows) or group/other (Unix). Fails as `ErrorCategory.Forbidden` with a copy-pasteable remediation command.
- `NotVerifiable`: File permissions cannot be queried due to underlying OS/IO errors.

## Consistency Check

- Unique coverage: Specifically validates *read* permissions on files containing unencrypted bootstrap secrets.
- Closest related use cases: `UC-001` only validates write and ownership safety; `UC-003` handles writing protected files initially.
- Contradictions: None found.

## Acceptance

- Manifests containing secrets fail startup if `chmod 644` or `chmod 640` is present on Unix, returning a technical message recommending `chmod 600`.
- Manifests containing secrets fail startup on Windows if `Authenticated Users` or `Users` have read access, returning a technical message recommending `icacls ... /inheritance:r ...`.
- No sensitive keys or passwords appear in error logs or exception payloads.
