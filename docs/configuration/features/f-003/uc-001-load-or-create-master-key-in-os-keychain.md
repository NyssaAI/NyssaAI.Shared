---
feature: F-003
id: UC-001
status: planned
---

# UC-001 Load or Create Master Key in OS Keychain

## Actor

Application Initialization / Configuration Client (`ConfigurationClient`, `KeyringManagedSecret`).

## Goal

Ensure the application has its 32-byte Master Encryption Key ready in the operating system's credential store (Windows Credential Manager, Apple Keychain, or Linux Secret Service) without saving raw encryption keys into plain files on disk.

## Starting Context

The application is starting up and resolving configuration settings. The application knows its identity (`ApplicationName`).

## Entry Route

Called automatically during client startup when initializing local encryption providers.

## Trigger

First resolution of the configuration service or client.

## Process

```text
Actor loads or provisions master encryption key
+-- Starting context: application identity known
+-- Entry route: KeyringManagedSecret.GetOrCreate called during startup
+-- Trigger: application initialization
+-- Step 1: Query OS keychain for application key
|   +-- Check address: service = <ApplicationName>, key = "configuration-key"
|   +-- Case A: Key exists in keychain
|   |   +-- Read and decode 32-byte key into memory
|   |   \-- Key is ready for use (Created: false)
|   +-- Case B: Key is confirmed absent (first run ever on this machine)
|   |   +-- Generate 32 bytes of secure random data
|   |   +-- Save key in OS keychain with local machine persistence
|   |   \-- Key is ready for use (Created: true)
|   \-- Case C: OS keychain is offline, locked, or returns an error
|       \-- Stop immediately with failure (StoreUnavailable)
|           \-- DO NOT generate a replacement key
+-- Step 2: Initialize AES-256-GCM cipher with master key
\-- Step 3: Zero out temporary raw key memory buffers
```

## Step Details

### Step 1: Query OS keychain for application key
- **Strict Absence Distinction:** A new key is created **only** when the operating system positively confirms that nothing exists at that address (`ERROR_NOT_FOUND` on Windows, `errSecItemNotFound` on macOS, or a null lookup on Linux).
- **Transient Glitch Protection:** If the credential store is temporarily unreachable or locked, the operation fails closed. The library never generates a replacement key during an outage, because replacing the master key would permanently make all previously encrypted files unreadable.
- **Memory Hygiene:** Once the cipher is constructed, intermediate byte buffers containing raw key material are cleared using zero-fill operations.

## Success

The master encryption key is loaded into memory or newly filed in the OS keychain, and the application is ready to encrypt or decrypt settings.

## Failure Cases

- `StoreUnavailable`: The OS keychain is locked, inaccessible, or failed to answer.
- `SecretMalformed`: An entry exists at that address in the keychain, but it does not contain a valid 32-byte base64-encoded key.

## Consistency Check

- Unique coverage: Covers obtaining or provisioning the single master key in the OS keychain.
- Closest related use cases: `UC-002` and `UC-003` use this key to encrypt and decrypt settings on disk; `UC-004` deletes the key; `UC-005` verifies that other user accounts cannot access it.
- Contradictions: None found.

## Acceptance

- On first run, a 32-byte key is created and saved in the OS keychain.
- On subsequent runs, the existing key is loaded without generating a new one.
- If the keychain cannot be reached, startup fails without overwriting the key.
