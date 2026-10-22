---
feature: F-204
id: UC-002
status: planned
---

# UC-002 Export and Import a Recovery Handle Across Process Restarts

## Actor

Application Process (`ConfigurationRecoveryHandle.Export`, `ConfigurationRecoveryHandle.Import`, `IConfiguration.RecoverAsync`).

## Goal

Save a `ConfigurationRecoveryHandle` as an encrypted, authenticated string token so an application or CLI tool can exit after a failed operation and safely resume recovery in a new process later.

## Starting Context

An interrupted `MoveAsync`, `SetAsAsync`, or `MergeSettingsAsync` produced a non-null `ConfigurationRecoveryHandle`.

## Entry Route

1. Export before exit: `string savedToken = handle.Export()`.
2. Import in a later process: `var restored = ConfigurationRecoveryHandle.Import(savedToken)`.
3. Resume: `var outcome = await configuration.RecoverAsync(restored)`.

## Trigger

The application wants to persist an unfinished recovery handle across a process restart or between separate CLI invocations.

## Process

```text
Actor exports recovery handle and resumes in a new process
+-- Starting context: caller holds a ConfigurationRecoveryHandle from an interrupted operation
+-- Entry route: handle.Export() -> ConfigurationRecoveryHandle.Import(token) -> RecoverAsync(restored)
+-- Trigger: application saves recovery token to application-owned storage and restarts
+-- Step 1: Export protected token
|   +-- Derive purpose-specific recovery key from the 32-byte master key via HKDF-SHA256
|   +-- Bind AAD to ApplicationName and canonical configurationDirectory
|   +-- Seal recovery state envelope with AES-256-GCM
|   \-- Return opaque Base64URL token string from handle.Export()
|       (Note: handle.ToString() returns a redacted placeholder so tokens are never logged accidentally)
+-- Step 2: Import token in later process
|   +-- Call ConfigurationRecoveryHandle.Import(savedToken)
|   \-- Construct handle wrapper (blank tokens fail immediately; cryptographic check happens in Step 3)
+-- Step 3: Authenticate and execute in RecoverAsync(restored)
|   +-- Derive HKDF recovery key for current ApplicationName and canonical configurationDirectory
|   +-- Decrypt and authenticate token:
|   |   +-- Token tampered with, truncated, or from a different app/directory/key
|   |   |   \-- Throw ResultException [Validation] before contacting any provider
|   |   \-- Token authentic -> Proceed with recovery execution (UC-001 / UC-003)
\-- Success: Interrupted operation is safely resumed and completed in the new process
```

## Step Details

### Step 1: Cryptographic Isolation and Redaction
- Recovery handles use a separate purpose subkey derived via HKDF-SHA256 from the application's protection key (`F-003`), with Additional Authenticated Data (AAD) binding the token to `ApplicationName` and the canonical `configurationDirectory`.
- Calling `handle.ToString()` returns a redacted string (`ConfigurationRecoveryHandle [redacted]`) so passing the handle to a logger never leaks the token.

## Success

The exported token is restored in a new process, authenticated against the application's key and configuration root, and executed to completion via `RecoverAsync`.

## Failure Cases

- `Validation`: The token is empty, malformed, tampered with, created by a different `ApplicationName`, bound to a different `configurationDirectory`, or opened after the OS keyring master key was replaced.

## Consistency Check

- Unique coverage: Covers `ConfigurationRecoveryHandle.Export()`, `Import(token)`, `ToString()` redaction, and HKDF cross-process authentication.
- Closest related use cases: `UC-001` and `UC-003` execute the recovery workflow using the handle.
- Contradictions: None found.

## Acceptance

- The ASCII process tree can be followed from the feature guide.
- Expected outputs are clear: exported handles round-trip through `Import` and `RecoverAsync`, `ToString()` is redacted, and tampered or cross-application handles fail authentication.
- Failure cases produce clear guidance.
- The consistency check shows no unresolved duplicate coverage or contradictions.
