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
- **Function**: Maps application names to their storage directories, configures document lock timeouts, and declares shared root-level external cloud key vaults (`keyVaults`).
- **Files**:
  - [`config-manifest.json`](config-manifest.json): Full multi-application manifest with a shared root `keyVaults` entry and custom timeout settings.
  - [`config-manifest.minimal.json`](config-manifest.minimal.json): Minimal manifest automatically provisioned on an application's first run (`keyVaults` omitted when empty).
  - [`config-manifest.vault.json`](config-manifest.vault.json): Advanced multi-vault manifest configuring Bitwarden Secrets Manager (project-scoped) and Azure Key Vault integrations at the root `keyVaults` level.

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
  "keyVaults": {
    "<VaultName>": {
      "kind": "<string>",
      "endpoint": "<URI string>",
      "credential": "<namespace/key>",
      "cacheTTLSeconds": 60,
      "settings": {
        "<key>": "<string value>"
      },
      "secrets": {
        "<key>": {
          "value": "<FormatVersion 2 AES-256-GCM Base64URL ciphertext>",
          "provider": "aesLocal",
          "encrypted": true
        }
      }
    }
  },
  "applications": {
    "<ApplicationName>": {
      "configurationDirectory": "<string>",
      "lockTimeoutMs": 10000
    }
  }
}
```

- **`version`** (`int`): Strictly `1`.
- **`keyVaults`** (`object`, optional): Shared root-level map of external cloud key vaults keyed by `<VaultName>` (cannot be blank, contain `/`, `\`, whitespace, or control characters, or use case-insensitively reserved names `"aesLocal"`, `"literal"`, `"oauth"`, `"default"`, `"plaintext"`). Omitted on disk when empty (`{}`).
  - Each entry defines `kind`, optional absolute `endpoint` URI, optional local OAuth `credential` pointer (`"ns/key"`), optional `cacheTTLSeconds` (`integer >= 0`, defaults to `60`, `0` disables caching; also accepted under `settings["cacheTTLSeconds"]`), optional `settings` map (omitted when empty), and optional `secrets` map (omitted when empty; redundant inner `"name"` is omitted when writing and must match `<VaultName>` if present).
  - **`secrets` Mandatory Encryption**: Every `keyVaults.<VaultName>.secrets.<secretKey>` entry MUST be an encrypted `aesLocal` envelope (`{"value": "<FormatVersion 2 Base64URL>", "provider": "aesLocal", "encrypted": true}`) sealed with the shared manifest master key (`service = "nyssa-ai-configuration"`, `account = "_manifest"`) and AAD `("keyVaults/<VaultName>", "<secretKey>")`. Plaintext strings are rejected with `Validation`.
  - **Layering**: Manifest `keyVaults` entries layer over programmatic `BootstrapOptions` vaults (`keyVault` / `keyVaults`) by `name`: a matching manifest vault replaces the programmatic vault whole (inheriting code `cacheTTLSeconds` only when omitted in the manifest), while distinct vault names coexist.
- **`applications`** (`object`): Map of application sections. Sibling `applications` and `keyVaults` entries are preserved during writes. Legacy per-application `applications.<ApplicationName>.keyVault` blocks are rejected with `Validation`.
- **`configurationDirectory`** (`string`): Absolute or manifest-relative directory where `<namespace>.json` files reside. Once recorded, this location is canonical.
- **`lockTimeoutMs`** (`integer`, recommended): Maximum wait duration for cross-process locks specified in integer milliseconds (e.g., `10000` for 10 seconds). For backward compatibility, libraries also accept `"lockTimeout": "hh:mm:ss"`. Defaults to 10000 ms (10 seconds) if omitted.
- **Permissions**: Whenever any `keyVaults.<VaultName>.secrets` (or legacy `applications.<ApplicationName>.keyVault.secrets`) entry is non-empty, `DirectorySafety.VerifyPrivateFile` (POSIX `0600` / Windows user-private ACL) runs **first** before validation and fails closed with `Forbidden` if readable by other accounts.

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
