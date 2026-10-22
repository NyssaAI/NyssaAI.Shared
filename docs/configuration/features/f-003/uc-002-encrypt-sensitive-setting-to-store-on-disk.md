---
feature: F-003
id: UC-002
status: planned
---

# UC-002 Encrypt Sensitive Setting to Store on Disk

## Actor

Configuration Store / Encryption Provider (`ConfigurationProviderLocalEncryption`, `ConfigurationCipherAesGcm`).

## Goal

Take a sensitive configuration value (like a password, secret token, or private key), encrypt it using AES-256-GCM and the master key from the OS keychain, and save the scrambled text into the local `.json` file bound to that specific section and setting name.

## Starting Context

The application has initialized its master encryption key in memory (`UC-001`). The caller wants to save a setting using the `"aesLocal"` provider.

## Entry Route

Invoked when saving settings via `SetAsync("namespace", "key", "secretValue", "aesLocal")` or keyed `SetAsAsync`.

## Trigger

An explicit write request for a configuration key specifying the `"aesLocal"` encryption provider.

## Process

```text
Actor encrypts setting and writes to local file
+-- Starting context: plaintext value, namespace, key name, master key loaded
+-- Entry route: SetAsync or SetAsAsync with provider "aesLocal"
+-- Trigger: application saves an encrypted secret
+-- Step 1: Generate fresh random 12-byte nonce
+-- Step 2: Build address metadata (AAD)
|   \-- Pack namespace name and key name into authenticated data
+-- Step 3: Encrypt value with AES-256-GCM
|   +-- Inputs: master key, nonce, plaintext UTF-8 bytes, address metadata
|   \-- Outputs: ciphertext bytes + 16-byte authentication tag
+-- Step 4: Assemble binary payload
|   \-- [Version byte 0x02][12-byte nonce][16-byte tag][ciphertext]
+-- Step 5: Encode payload into URL-safe text (Base64URL without padding)
+-- Step 6: Store entry in <namespace>.json
|   \-- {"value": "<scrambled_text>", "encrypted": true, "provider": "aesLocal"}
\-- Success: Encrypted setting is safely persisted to disk
```

## Step Details

### Step 2: Build address metadata (AAD)
- The namespace and key name are baked into the encryption authentication tag as Additional Authenticated Data (AAD).
- Because the address is tied directly to the ciphertext's cryptographic signature, an attacker or corrupted process cannot copy an encrypted password from one setting (e.g. `mail/password`) and paste it into another setting (e.g. `admin/apiKey`). It will only open under the exact section and key where it was created.

### Step 5: Encode payload
- The payload uses unpadded Base64URL encoding so it can be stored cleanly as standard text inside a JSON document without escaping issues.

## Success

The secret is encrypted and stored in the local `.json` file. The plaintext password is never written to disk.

## Failure Cases

- `KeyLengthInvalid`: The cipher was configured with an invalid key length (not 32 bytes).
- `FileNotWritable`: File permissions or disk errors prevented writing to the local `.json` document.

## Consistency Check

- Unique coverage: Covers encrypting a plaintext value and storing it in a local JSON document.
- Closest related use cases: `UC-003` decrypts this value back to plaintext; `UC-001` provides the key.
- Contradictions: None found.

## Acceptance

- Stored JSON entry contains `"encrypted": true` and `"provider": "aesLocal"`.
- The value on disk is scrambled Base64URL text; the plaintext secret is not present in the file.
- Relocating the ciphertext to a different key or section makes it invalid.
