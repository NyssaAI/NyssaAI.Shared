---
feature: F-003
id: UC-005
status: planned
---

# UC-005 Prevent Different User Account from Reading Secrets

## Actor

Second User Account on the same machine ("User B") or Unauthorized Local Process.

## Goal

Ensure that a different user account on the same operating system cannot retrieve User A's master encryption key from the keychain or decrypt User A's stored configuration files.

## Starting Context

- **User A** has run the application. User A's Master Encryption Key is stored in User A's personal OS keychain. User A's configuration directory contains files with encrypted settings.
- **User B** is logged in as a separate, standard user account on the same computer.

## Entry Route

User B launches the application, or runs a tool attempting to read User A's configuration files or master key.

## Trigger

User B attempts to access settings or configuration files belonging to User A.

## Process

```text
Unauthorized user attempts to access secrets
+-- Starting context: User A's secrets exist; User B is running under a separate account
+-- Entry route: User B runs the app or attempts to read files
+-- Trigger: User B queries configuration or keychain
+-- Defense Layer 1: OS Keychain Isolation
|   +-- User B's process queries OS keychain for "AppName/configuration-key"
|   +-- OS keychain (Windows Credential Manager / Mac Keychain / Linux Secret Service)
|   |   is partitioned per logged-in user profile
|   \-- Result: OS reports key NOT FOUND in User B's keychain
|       \-- User B cannot read User A's master key
+-- Defense Layer 2: Independent Key Generation Does Not Help
|   +-- User B's app generates a brand new key in User B's own keychain
|   \-- User B now has Key_B in their keychain (User A's files were encrypted with Key_A)
+-- Defense Layer 3: File System Access & Decryption Failure
|   +-- Case 3A: User B attempts to read User A's directory directly
|   |   \-- Blocked by filesystem permissions (UC-001 under F-001) with Access Denied
|   \-- Case 3B: User B obtains a copy of User A's encrypted .json file
|       +-- User B runs app attempting to decrypt entry using Key_B
|       +-- AES-256-GCM authentication check executes with Key_B
|       \-- Decryption fails immediately with SealedValueNotAuthentic
\-- Success: User A's secrets remain completely protected and unreadable to User B
```

## Step Details

### Defense Layer 1: OS Keychain Isolation
- Operating systems partition credential storage strictly by user session/profile:
  - **Windows:** Windows Credential Manager generic credentials created with `CRED_PERSIST_LOCAL_MACHINE` are bound to the current user SID's profile. Other standard user accounts cannot read them.
  - **macOS:** Apple Keychain Services limits access to the user's login keychain.
  - **Linux:** Secret Service / D-Bus sessions are scoped to the user's login session bus.

### Defense Layer 3: Cryptographic Independence
- Even if User B runs the application and successfully initializes their own encryption setup, the library generates a completely distinct, random 32-byte key (`Key_B`).
- When `ConfigurationCipherAesGcm.Open` is invoked on User A's ciphertext using `Key_B`, the AES-256-GCM authentication tag verification fails. The library returns `SealedValueNotAuthentic` (`ErrorCategory.Validation`) and yields zero plaintext data.

## Success

User A's secrets remain completely secure: User B cannot access User A's master key in the keychain, and even with access to the ciphertext, User B cannot decrypt the data.

## Failure Cases

- `SealedValueNotAuthentic`: The ciphertext was sealed with a different key and cannot be opened.
- `Forbidden` / `AccessDenied`: Filesystem ACLs prevent User B from reading the file directory.

## Consistency Check

- Unique coverage: Covers multi-user security boundaries and cryptographic isolation between different OS accounts.
- Closest related use cases: `UC-001` provisions the key; `UC-003` handles decryption when the correct key is available.
- Contradictions: None found.

## Acceptance

- A user process running under User B cannot read generic credentials created by User A.
- Attempting to decrypt User A's ciphertext using User B's generated key fails authentication.
- No sensitive plaintext is revealed in error messages.
