---
id: F-001
phase: 0
status: planned
---

# F-001 Protected File System & Cross-Process Locking

## Summary

Provides stateless filesystem security controls (`DirectorySafety` and `ProtectedFileSystem`) and cross-process `.lock` file coordination for configuration manifests and namespace JSON documents across Windows, Linux, and macOS.

## User Need

Application developers and system operators need assurance that local configuration directories, namespace files, and secret-bearing manifests cannot be modified (or read, when holding bootstrap secrets) by unauthorized OS accounts, and that concurrent processes never corrupt JSON files with torn writes.

## Use Cases

- [UC-001: Verify Directory and File Write Safety](uc-001-verify-directory-and-file-write-safety.md)
- [UC-002: Verify Private Manifest Permissions](uc-002-verify-private-manifest-permissions.md)
- [UC-003: Provision Directory and Atomically Publish Files](uc-003-provision-directory-and-atomic-publish.md)
- [UC-004: Coordinate Concurrent Access via Bounded Cross-Process Lock](uc-004-coordinate-concurrent-access-via-bounded-cross-process-lock.md)

## Scope

- Creating configuration directories with restrictive permissions (`0700` on POSIX; current user, `SYSTEM`, and `Administrators` with inheritance disabled on Windows).
- Validating existing directories and files (`DirectorySafety.VerifyDirectory`, `VerifyFile`, `VerifyPrivateFile`) and failing closed with actionable remediation messages when group/other write or unauthorized ACL entries are present.
- Enforcing private-file read restrictions (`0600` on POSIX; no non-owner/non-admin read ACEs on Windows) when a manifest section contains `keyVault.secrets`.
- Publishing file updates atomically by writing to a restricted temporary file (`0600`) in the target directory and renaming/replacing it into place.
- Acquiring and releasing exclusive cross-process sidecar lock files (`<target>.lock`) with exponential backoff up to a configurable `lockTimeout` (default 10 seconds).
- Deliberately exposing `DirectorySafety` and `ProtectedFileSystem` as non-substitutable security controls so test doubles cannot accidentally bypass permission checks in production compositions.

## Out of Scope

- Inspecting Unix `uid`/`gid` ownership (POSIX checks validate permission bits only).
- Parsing or validating JSON schemas inside manifests or namespace documents (owned by `F-002` and `F-101`).
- Encrypting or decrypting file contents (owned by `F-003`).

## Inputs

- Target directory path, manifest file path, or namespace document path.
- Lock timeout duration (`TimeSpan` / milliseconds).
- Platform OS identity (Windows vs. POSIX) and current user SID/permissions.

## Outputs

- Verified or newly created restricted directories and files on disk.
- Exclusive lock handle (`IDisposable` / `AsyncDisposable` or release callback) guarding critical sections.
- Deterministic `Result` failures (`Forbidden` / `AccessDenied` / `Timeout`) naming the unsafe path, offending permission bit or principal, and remediation command (without leaking secret values).

## Acceptance

- New directories are created with `0700` on POSIX and restricted owner/SYSTEM/Administrators DACLs on Windows.
- Unsafe existing directories or secret-bearing manifests readable/writable by unauthorized principals fail verification before reading or writing data.
- Concurrent writers across processes serialize cleanly via `<file>.lock` and never leave partial JSON files on disk.
- Both `.NET` (`NyssaAI.Configuration`) and `TypeScript/Node.js` (`@nyssa-ai/configuration`) implementations pass all unit and isolation tests for file security and locking.
- The path contract matches `.plan-product/config.json`.

## Dependencies

None (foundational capability).
