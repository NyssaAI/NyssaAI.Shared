# Encryption

Owns address-bound encryption for sensitive configuration entries. `IConfigurationCipher` defines the
cryptographic operation consumed by the local encryption provider; `ConfigurationCipherAesGcm` implements
AES-256-GCM sealing and authenticated opening.

`ConfigurationProviderLocalEncryption` adapts the cipher to the pluggable provider contract in `Contracts`. The
feature owns ciphertext creation and validation, but not document persistence or provider
registration.

`IEncryption` is this feature's covering contract: a caller opens a cipher and the local-encryption provider through it, never through `ConfigurationCipherAesGcm` directly.
