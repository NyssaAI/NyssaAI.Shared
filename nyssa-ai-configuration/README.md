# @nyssa-ai/configuration

`@nyssa-ai/configuration` provides secure, cross-process locked, AES-256-GCM encrypted configuration and secret storage for TypeScript and Node.js applications and CLI tools, with on-disk format and cryptographic parity with `NyssaAI.Configuration` (.NET).

## Features

- **Unified Configuration API (`IConfiguration`)**: Read and write plain settings, locally encrypted secrets (`"aesLocal"`), OAuth 2.0 PKCE credentials (`"oauth"`), and external cloud vault pointers through a consistent `Result<T>` contract without exception-driven control flow.
- **Cross-Platform Parity with .NET**: Shares the `~/.nyssa-ai/config-manifest.json` layout, `<namespace>.json` envelope structure, cross-process `.lock` coordination, and FormatVersion 2 AES-256-GCM ciphertext authenticated with `(namespace, key)` AAD.
- **Address-Bound Local Encryption**: Encrypts secrets locally with AES-256-GCM using a 256-bit master key managed via `IKeyring` (`KeyringProtectedFile`, `KeyringOsPlatform`, or `InMemoryKeyring`).
- **Typed DTO & Namespace Operations**: Save and read typed objects under a single key (`setAs` / `getAs`), replace an entire namespace from a DTO (`setNamespace` / `getNamespace`), or merge partial updates while preserving existing providers (`mergeSettings`).
- **Interactive OAuth 2.0 + PKCE & Silent Refresh**: Acquire OAuth tokens via `authorize`, store encrypted credentials on disk, automatically refresh expiring tokens under cross-process namespace locks, and replay pending rotated grants after interrupted writes.
- **External Vault Adaptation & Guarded Recovery**: Plug in cloud secret managers (`IVaultProviderFactory` / `IVaultProvider`), migrate entries between providers with `move`, and resume interrupted multi-step writes with portable `ConfigurationWriteRecoveryHandle` tokens (`recoverWrite`).

## Installation

```bash
npm install @nyssa-ai/configuration
```

## Quick Start

```typescript
import { ConfigurationClient } from "@nyssa-ai/configuration";

// Initialize the client (provisions manifest and protected config directory on first run)
const clientRes = await ConfigurationClient.initialize({
  applicationName: "OrdersService"
});

if (!clientRes.ok) {
  throw new Error(`Startup failed: ${clientRes.error}`);
}

const config = clientRes.value;

// 1. Store and read a plaintext setting
await config.set("mail", "host", "smtp.example.com");
const hostRes = await config.get("mail", "host");
// hostRes.ok === true -> hostRes.value === "smtp.example.com"

// 2. Store an AES-256-GCM locally encrypted secret
await config.set("mail", "password", "SuperSecretP@ss!", {
  encrypted: true,
  provider: "aesLocal"
});

// Subsequent updates preserve the recorded provider ("aesLocal") automatically
await config.set("mail", "password", "RotatedSecretP@ss!");

const passRes = await config.get("mail", "password");
// passRes.ok === true -> passRes.value === "RotatedSecretP@ss!"
```

## Working with Typed Settings & Namespaces

### Whole Namespace as a Typed DTO

Decompose top-level properties into individual keys within `<namespace>.json`, allowing different properties in the same DTO to use different storage providers:

```typescript
interface SmtpSettings {
  host: string;
  port: number;
  enableSsl: boolean;
}

// Replace namespace contents
await config.setNamespace<SmtpSettings>("smtp", {
  host: "smtp.example.com",
  port: 587,
  enableSsl: true
});

// Partial update (preserves omitted keys and existing encryption providers)
await config.mergeSettings("smtp", {
  port: 465
});

// Assemble the complete DTO across all entries in the namespace
const smtpRes = await config.getNamespace<SmtpSettings>("smtp");
if (smtpRes.ok) {
  console.log(smtpRes.value.host, smtpRes.value.port);
}
```

### Single Key as a Typed DTO

Store and protect an entire object under a single key:

```typescript
await config.setAs("mail", "server", {
  host: "smtp.example.com",
  port: 587
});

const serverRes = await config.getAs<{ host: string; port: number }>("mail", "server");
```

## Provider Migration (`move`)

To change where an existing entry is stored (for example, migrating a plaintext value to `"aesLocal"` or an external vault, or back to `"Default"`), use `move`:

```typescript
// Encrypt an existing plaintext setting in place
const moveRes = await config.move("db", "connectionString", "aesLocal");
if (moveRes.ok) {
  console.log("Source copy retained?", moveRes.value.sourceValueRemains);
}
```

## Configuration & Defaults

`ConfigurationClient.initialize(options?)` accepts [`ConfigurationOptions`](./src/ConfigurationOptions.ts):

| Option | Default | Description |
| --- | --- | --- |
| `applicationName` | Inferred from `package.json` `name` or `"NyssaApp"` | Identifies the application section in the manifest and the keyring service name. |
| `configManifest` | `~/.nyssa-ai/config-manifest.json` | Path to the configuration manifest JSON file. |
| `configDirectory` | `<cwd>/.nyssa-ai/config` | Default configuration directory recorded in the manifest on first use. |
| `lockTimeoutMs` | `10000` (10s) | Cross-process document lock acquisition timeout in milliseconds. |
| `keyring` | `KeyringProtectedFile` | Master key custody strategy (`KeyringProtectedFile`, `KeyringOsPlatform`, or `InMemoryKeyring`). |
| `keyVault` | `undefined` | Optional external vault configuration (overridden whole if the manifest defines `keyVault`). |
| `vaultFactories` | `[]` | Registered `IVaultProviderFactory` implementations matched by `keyVault.kind`. |
| `logger` | `NullConfigurationLogger` | Optional structured operational logger (`IConfigurationLogger`). |

## Subpath Imports

The package exports `ConfigurationClient` at the root entrypoint and exposes all public contracts and adapters via subpath imports:

```typescript
import { ConfigurationClient } from "@nyssa-ai/configuration";
import { InMemoryKeyring } from "@nyssa-ai/configuration/keyring/InMemoryKeyring";
import { KeyringOsPlatform } from "@nyssa-ai/configuration/keyring/KeyringOsPlatform";
import { ConfigurationWriteRecoveryHandle } from "@nyssa-ai/configuration/recovery/ConfigurationWriteRecoveryHandle";
import type { IConfiguration } from "@nyssa-ai/configuration/IConfiguration";
import type { Result } from "@nyssa-ai/configuration/results/Result";
```

## Licensing

Licensed under the [Apache License, Version 2.0](./LICENSE.txt).

