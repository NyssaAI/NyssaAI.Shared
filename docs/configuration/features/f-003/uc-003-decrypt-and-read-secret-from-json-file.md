---
feature: F-003
id: UC-003
status: planned
---

# UC-003 Decrypt and Read Secret from JSON File

## Actor

Application Code / Configuration Store (`ConfigurationStore`, `ConfigurationCipherAesGcm`).

## Goal

Read an encrypted setting from a local `.json` file, verify that it has not been tampered with or moved to a different setting name, decrypt it with the master key from the OS keychain, and return the original plaintext value.

## Starting Context

A configuration file (`<namespace>.json`) exists on disk and contains an entry with `"encrypted": true` and `"provider": "aesLocal"`. The master key is loaded in memory (`UC-001`).

## Entry Route

Invoked when application code requests a setting via `GetAsync("namespace", "key")` or `GetAsAsync`.

## Trigger

An application read request for an encrypted configuration setting.

## Process

```text
Actor reads and decrypts setting from local file
+-- Starting context: encrypted entry in <namespace>.json, master key in memory
+-- Entry route: GetAsync(namespace, key) called
+-- Trigger: application requests secret value
+-- Step 1: Read entry from <namespace>.json
|   +-- Entry not found -> Fail with NotFound error
|   \-- Entry found -> Inspect provider metadata ("aesLocal", encrypted: true)
+-- Step 2: Decode Base64URL payload into bytes
|   \-- Invalid base64 text -> Fail with SealedValueMalformed
+-- Step 3: Validate header
|   +-- Require length >= 29 bytes (version + nonce + tag)
|   +-- Require format version byte == 0x02
|   \-- Check fails -> Fail with SealedValueMalformed
+-- Step 4: Reconstruct address metadata (AAD)
|   \-- Pack requested namespace and key name
+-- Step 5: Decrypt with AES-256-GCM
|   +-- Tag verification passes:
|   |   \-- Plaintext bytes decrypted and decoded to UTF-8 string
|   \-- Tag verification fails (tampered file, wrong key, or moved setting):
|       \-- Fail immediately with SealedValueNotAuthentic
\-- Success: Original plaintext secret returned to application
```

## Step Details

### Step 5: Decrypt with AES-256-GCM
- **Tampering & Relocation Detection:** AES-256-GCM verifies that neither the ciphertext, the nonce, the version, nor the associated data (`namespace` and `key`) have been altered. If someone modified the file on disk or copied an encrypted value from another key, the cryptographic tag check fails and the library returns `SealedValueNotAuthentic` (`ErrorCategory.Validation`).
- **Clean In-Memory Resolution:** The returned plaintext string lives only in application memory and is never written out to temporary files or logged.

## Success

The application receives the original plaintext string exactly as it was when originally saved.

## Failure Cases

- `NotFound`: The requested namespace or key does not exist.
- `SealedValueMalformed`: The stored text is corrupted, truncated, or uses an unsupported cipher format version.
- `SealedValueNotAuthentic`: Decryption failed because the data was altered, encrypted with a different key, or moved from a different namespace or key.

## Consistency Check

- Unique coverage: Covers reading and decrypting stored secrets into plaintext.
- Closest related use cases: `UC-002` encrypts the value originally; `UC-005` handles what happens when a different user account attempts this read.
- Contradictions: None found.

## Acceptance

- Successfully returns original plaintext for authentic entries.
- Changing a single character in the encrypted text causes decryption to fail.
- Attempting to decrypt the value under a different namespace or key name fails authentication.
