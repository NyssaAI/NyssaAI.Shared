# NyssaAI Configuration Component Specifications & Examples

This directory contains specifications and canonical examples of the configuration files defined and managed by the **NyssaAI Configuration Component** (`NyssaAI.Configuration`, `@nyssa-ai/configuration`, and `nyssa-ai-configuration`).

---

## 1. Component File System Architecture

The configuration component interacts with two levels of files:

```text
Component Manifest (Owned by the Configuration Component)
└── ~/.nyssa-ai/
    ├── config-manifest.json        <-- Tier 1: Canonical Bootstrap Manifest
    └── config-manifest.json.lock   <-- Sidecar handle lock for atomic manifest provisioning

Application Namespaces (Stored in the Application's configurationDirectory)
└── <configurationDirectory>/       <-- Tier 2: Namespace Store (Defined in Manifest)
    ├── <namespace>.json            <-- Per-namespace envelope document (e.g., database.json)
    └── <namespace>.json.lock       <-- Kernel handle-based non-unlinking lock file
```

For a complete side-by-side example of a single hypothetical application (`AcmeCommerce`) with two named components (`checkout-api` and `fulfillment-worker`) across **Linux**, **Windows**, and **macOS** — showing where the running code, `config-manifest.json`, `<namespace>.json` files, and OS Keyring entries reside — see [`platform-filesystem-layout.md`](platform-filesystem-layout.md).

---

## 2. Configuration Component Files (`docs/specs/config/`)

These files define the operational schema of the configuration component itself:

### A. The Bootstrap Manifest (`config-manifest.json`)
The central file read and written by the configuration component to bootstrap applications and discover their storage:
- **Default Path**: `~/.nyssa-ai/config-manifest.json` (where `~` is the user profile root).
- **Function**: Maps application names to their storage directories, configures document lock timeouts, and declares external cloud key vaults.
- **Files**:
  - [`config-manifest.json`](config-manifest.json): Full multi-application manifest with cloud vault and custom timeout settings.
  - [`config-manifest.minimal.json`](config-manifest.minimal.json): Minimal manifest automatically provisioned on an application's first run.
  - [`config-manifest.vault.json`](config-manifest.vault.json): Advanced manifest configuring Bitwarden Secrets Manager and Azure Key Vault integrations.

### B. Namespace Document Envelope (`namespace-envelope.template.json`)
- **File**: [`namespace-envelope.template.json`](namespace-envelope.template.json)
- **Function**: The canonical specification template for any `<namespace>.json` file managed by the store, illustrating the compact metadata serialization rules (`value`, `provider`, `encrypted`, `reauth_required`, `json`), including scalar primitives, serialized DTOs under a single key, local encryption, cloud vaults, and OAuth grants.

---

## 3. Example Application Namespaces (`docs/specs/config/namespaces/`)

Application data (such as database credentials, API keys, or mail server settings) is stored inside the namespace envelope format defined by the component. Concrete illustrative examples are organized in the [`namespaces/`](namespaces/) subdirectory:

| File | Namespace Pattern Demonstrated |
|---|---|
| [`namespaces/appsettings.json`](namespaces/appsettings.json) | Plaintext literals (strings, numbers, booleans, arrays, nested objects). |
| [`namespaces/database.json`](namespaces/database.json) | Mixed namespace combining plaintext host/port with an `aesLocal` encrypted password. |
| [`namespaces/mailserver.json`](namespaces/mailserver.json) | Multi-provider storage (plaintext, `aesLocal` encryption, and cloud vault pointer). |
| [`namespaces/auth.json`](namespaces/auth.json) | OAuth 2.0 PKCE sealed tokens (`provider: "oauth"`) and `reauth_required: true`. |
| [`namespaces/service-dto-single-key.json`](namespaces/service-dto-single-key.json) | Whole-DTO configuration stored under a single key (`json: true`). |
| [`namespaces/service-dto-decomposed.json`](namespaces/service-dto-decomposed.json) | Namespace DTO with decomposed top-level properties. |
| [`namespaces/typed-strings.json`](namespaces/typed-strings.json) | String literal quoting and JSON disambiguation examples. |
| [`namespaces/legacy-namespace-settings.json`](namespaces/legacy-namespace-settings.json) | Backwards-compatible single-entry `namespaceSettings` format. |
| [`namespaces/database.json.lock`](namespaces/database.json.lock) | Sidecar non-unlinking handle lock file. |

---

## 4. Manifest Schema Reference

```json
{
  "version": 1,
  "applications": {
    "<ApplicationName>": {
      "configurationDirectory": "<string>",
      "lockTimeoutMs": 10000,
      "keyVault": {
        "name": "<string>",
        "kind": "<string>",
        "endpoint": "<URI string>",
        "credential": "<namespace/key>",
        "settings": {
          "<key>": "<string value>"
        },
        "secrets": {
          "<key>": "<string secret value>"
        }
      }
    }
  }
}
```

- **`version`** (`int`): Strictly `1`.
- **`applications`** (`object`): Map of application sections. Sibling sections are preserved during writes.
- **`configurationDirectory`** (`string`): Absolute or manifest-relative directory where `<namespace>.json` files reside. Once recorded, this location is canonical.
- **`lockTimeoutMs`** (`integer`, recommended): Maximum wait duration for cross-process locks specified in integer milliseconds (e.g., `10000` for 10 seconds). For backward compatibility, libraries also accept `"lockTimeout": "hh:mm:ss"`. Defaults to 10000 ms (10 seconds) if omitted.
- **`keyVault`** (`object`, optional): Replaces code-level vault options in full. Contains `name`, `kind`, optional HTTPS `endpoint`, local OAuth `credential` pointer (`"ns/key"`), `settings` map, and `secrets` map.
- **Permissions**: If `keyVault.secrets` has 1+ entries, POSIX `0600` / Windows private-file restrictions are strictly enforced.

---

## 5. Namespace Envelope Specification

Every `<namespace>.json` file contains a dictionary of keys mapping to entry envelopes:

```json
{
  "<SettingKey>": {
    "value": "<literal, primitive, or reference>",
    "provider": "<provider-name>",
    "encrypted": true,
    "reauth_required": true,
    "json": true
  }
}
```

### Compact Serialization Rules
- `provider`: Omitted if plaintext/literal.
- `encrypted`: Omitted if `false`.
- `reauth_required`: Omitted if `false`.
- `json`: Omitted if `false`.
