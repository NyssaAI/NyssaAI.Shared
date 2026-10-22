---
id: F-003
phase: 0
status: planned
---

# F-003 OS Keyring Custody & Address-Bound Local Encryption

## Summary

Manages the application's 256-bit local protection key in the operating system credential store (`IKeyring`) with strict non-destructive key custody (`KeyringManagedSecret`), and provides the `"aesLocal"` provider using FormatVersion 2 AES-256-GCM authenticated encryption bound to `(namespace, key)`.

## User Need

Applications need to encrypt sensitive values on disk without storing the encryption key alongside the configuration files, without risking accidental key replacement during a transient OS keyring failure, and without allowing an attacker to swap encrypted values between keys or namespaces.

## Use Cases

- [UC-001: Load or Create Master Key in OS Keychain](uc-001-load-or-create-master-key-in-os-keychain.md)
- [UC-002: Encrypt Sensitive Setting to Store on Disk](uc-002-encrypt-sensitive-setting-to-store-on-disk.md)
- [UC-003: Decrypt and Read Secret from JSON File](uc-003-decrypt-and-read-secret-from-json-file.md)
- [UC-004: Remove Master Key from Keychain](uc-004-remove-master-key-from-keychain.md)
- [UC-005: Prevent Different User Account from Reading Secrets](uc-005-prevent-different-user-account-from-reading-secrets.md)

## Scope

- Platform `IKeyring` port (`GetSecret`, `SetSecret`, `DeleteSecret`) and platform adapters for Windows Credential Manager (`CredReadW`/`CredWriteW`/`CredDeleteW`), macOS Keychain (`SecItem*`), and Linux FreeDesktop Secret Service (`libsecret`).
- Strict three-way outcome distinction in every `IKeyring` adapter: found secret bytes, confirmed absence (`null` only on `ERROR_NOT_FOUND`, `errSecItemNotFound`, or null `libsecret` lookup without `GError`), or operational failure.
- `KeyringManagedSecret` custody at service `<ApplicationName>`, account `"configuration-key"`: generates and stores a 32-byte random key only on confirmed absence; never overwrites or regenerates a key on read error; zeroes raw key buffers after cipher initialization.
- `ConfigurationCipherAesGcm` (`IConfigurationCipher`) implementing FormatVersion 2 AES-256-GCM sealing and opening:
  - Binary layout: `[0x02 (1 byte)][12-byte random nonce][16-byte authentication tag][ciphertext]`, encoded as Base64URL without padding.
  - Additional Authenticated Data (AAD): `[4-byte big-endian UTF-8 byte length of namespace][namespace UTF-8 bytes][4-byte big-endian UTF-8 byte length of key][key UTF-8 bytes]`.
- `ConfigurationProviderLocalEncryption` (`"aesLocal"` provider) adapting `IConfigurationCipher` to `IConfigurationProvider`.

## Out of Scope

- Storing or parsing `<namespace>.json` files (owned by `F-101`).
- Deriving HKDF subkeys for recovery handles (owned by `F-204`).
- Encrypting or refreshing OAuth grants directly (owned by `F-202`, which delegates sealing to `IConfigurationCipher`).

## Inputs

- `ApplicationName` and injected or default `IKeyring` instance.
- Entry address `(namespace, key)` and plaintext UTF-8 string or Base64URL ciphertext payload.

## Outputs

- 256-bit key persisted in the OS credential store at `(<ApplicationName>, "configuration-key")`.
- Sealed `ConfigurationValue` entries with `encrypted: true` and `provider: "aesLocal"`, or decrypted plaintext strings on read.
- Startup failure (`InvalidOperationException` / failed `Result`) if the OS credential store is unavailable or returns an invalid key length.

## Acceptance

- Ciphertexts produced by `NyssaAI.Configuration` (.NET) and `@nyssa-ai/configuration` (TypeScript/Node.js) are byte-for-byte wire-compatible under the same 256-bit key and `(namespace, key)` address.
- Modifying a single bit of ciphertext, nonce, tag, format version, `namespace`, or `key` fails authentication during decryption.
- A transient `IKeyring` read failure fails startup without calling `SetSecret` or overwriting the existing key.
- Key material buffers are zeroed after use.
- The path contract matches `.plan-product/config.json`.

## Dependencies

- [F-001](../../features/f-001/feature.md)
