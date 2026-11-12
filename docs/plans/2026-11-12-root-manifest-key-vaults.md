---
title: "Root-Level Manifest Key Vaults with Mandatory AES-256-GCM Secret Encryption"
status: "ready"
created: "2026-11-12"
updated: "2026-11-12"
repos:
  - "NyssaAI/NyssaAI.Shared"
  - "NyssaAI/vps-operations"
---

# Implementation Plan: Root-Level Manifest Key Vaults & Mandatory Secret Encryption

## 1. Objective & Resolved Decisions

Promote cloud key vault configuration in `~/.nyssa-ai/config-manifest.json` from a single per-application `applications.<app>.keyVault` object to a shared, multi-vault root `keyVaults` map (`Record<string, VaultManifestEntry>`), enforce mandatory AES-256-GCM encryption for all `keyVaults.<vaultName>.secrets` entries on disk, wire multi-vault bootstrap across **C#** (`NyssaAI.Configuration`), **TypeScript** (`@nyssa-ai/configuration`), and **Python** (`nyssa-ai-configuration`), and extend **`vps-operations`** (`nyssa-secrets`) with root vault management (`vault set`, `vault list`, `vault remove`) and provider migration (`move`) for multi-project Bitwarden Secrets Manager (`bws-corporate`, `bws-prod`, `bws-staging`).

---

## 2. Canonical Cross-Language Specification (Adversarially Verified)

### 2.1 On-Disk `config-manifest.json` Schema (`version: 1`)

```json
{
  "version": 1,
  "keyVaults": {
    "bws-corporate": {
      "kind": "bitwarden-secrets",
      "cacheTTLSeconds": 60,
      "settings": {
        "organizationId": "11111111-2222-3333-4444-555555555555",
        "projectId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
      },
      "secrets": {
        "accessToken": {
          "value": "Ag...",
          "provider": "aesLocal",
          "encrypted": true
        }
      }
    }
  },
  "applications": {
    "vps-agent": {
      "configurationDirectory": "/root/.nyssa-ai/vps-agent",
      "lockTimeoutMs": 10000
    }
  }
}
```

#### Wire & Validation Invariants (Identical Across C#, TS, Python)
1. **`0600` Private-File Gate Runs FIRST in `Read` / `readManifest` / `read_manifest` (`P0-3`):**
   - Immediately after parsing the root JSON object, if `keyVaults` is an object where ANY `keyVaults.<vault>.secrets` property is present and non-null / non-empty (even if malformed or plaintext!), OR if any legacy `applications.<app>.keyVault.secrets` is present and non-null / non-empty, call `DirectorySafety.VerifyPrivateFile(manifestPath)` **FIRST** and return `Forbidden` (`chmod 600` / `icacls`) before running legacy-section or envelope validation. Never include any secret or ciphertext value in error messages.
2. **Legacy Per-App `keyVault` Rejection:**
   - If `applications.<app>` contains a non-null `"keyVault"` property (in C#: `section.KeyVault is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined }`), fail closed with `Validation`:
     `manifest: applications.<app>.keyVault is no longer supported; move vault definitions to root 'keyVaults'`
3. **Compact Serialization (`P1-2`):**
   - Omit `"keyVaults"` on disk when empty (`{}`).
   - Omit `"settings"` and `"secrets"` on `keyVaults.<vaultName>` when empty (`{}`).
   - Omit redundant `"name"` inside `keyVaults.<vaultName>` when writing (in C#, `PrepareForWrite` sets empty `KeyVaults`, `Settings`, `Secrets`, `VaultManifestEntry.Name`, and `ApplicationManifestEntry.KeyVault` to `null` before `JsonSerializer.Serialize`).
   - Preserve sibling `keyVaults` and `applications` sections across all manifest writes.
4. **Vault Map Key & Fields (`P2-2`):**
   - Apply the same validation rules to both manifest `keyVaults` and programmatic `VaultOptions` (`keyVault` / `keyVaults`):
     - `vaultName`: non-blank, trimmed (`vaultName.trim() == vaultName`), no `/`, `\`, whitespace, or control chars (`[\x00-\x1f\x7f]`), and NOT reserved (`"aesLocal"`, `"literal"`, `"oauth"`).
     - Optional `"name"` inside `keyVaults.<vaultName>`: if present and `!= vaultName`, fail with `Validation`. Normalized in memory to `vaultName`.
     - `kind`: required non-blank string, no control chars.
     - `endpoint`: optional string; must parse as an absolute URI (`http:`/`https:` or valid scheme+authority).
     - `credential`: optional string; after stripping optional `oauth://`, must split into two valid segments `<namespace>/<key>`.
     - `cacheTTLSeconds`: optional integer `>= 0` (validated in `Read`/`readManifest`/`read_manifest` as well as `ReadApplication`); if `settings["cacheTTLSeconds"]` is present, must match `^\d+$`.
5. **Mandatory Encrypted `ManifestSecretEnvelope` (`P1-3`, `P3-1`):**
   - Every entry `keyVaults.<vaultName>.secrets.<secretKey>` on disk MUST be a JSON object `{ "value": string, "encrypted": true, "provider"?: "aesLocal" }` (in C#, raw wire DTO uses `JsonElement? Secrets` so string values never throw `JsonException` before validation, plus `[JsonSerializable(typeof(ManifestSecretEnvelope))]` and `[JsonSerializable(typeof(Dictionary<string, ManifestSecretEnvelope>))]` on `ManifestJsonContext` for clean serialization).
   - Reject raw strings, `null`, arrays, numbers, `encrypted !== true`, or `provider` present and `!== "aesLocal"` with `Validation`:
     `manifest: keyVaults.<vaultName>.secrets.<secretKey> must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json`
   - Strict structural FormatVersion 2 check in `ConfigurationManifestReader`: `value` must match `^[A-Za-z0-9_-]+$` with `len(value) % 4 != 1`, Base64URL-decode to `>= 29` bytes, and have `decoded[0] == 0x02`.
   - Redact `ToString()` / `repr()` (`repr=False` in Python dataclasses, custom `ToString()` on C# `ManifestSecretEnvelope`) so secret/ciphertext payloads never leak in debug/assertion formatting.

### 2.2 Manifest Master Key Custody & Cryptographic Isolation (`P0-1`, `P0-2`, `P1-1`, `P1-8`)

1. **Keyring Service & Account:**
   - `service = "nyssa-ai-configuration"`
   - `account = "_manifest"` (`MANIFEST_KEY_ACCOUNT` / `MANIFEST_ACCOUNT` / `ManifestKeyAccount = "_manifest"`).
2. **`KeyringProtectedFile` Manifest Directory Support (`P0-1`):**
   - In Python (`KeyringProtectedFile(configuration_directory, manifest_directory=None)`) and TypeScript (`new KeyringProtectedFile(configurationDirectory, manifestDirectory?)`):
     - When `account == "_manifest"`, read/write `(manifest_directory ?? configuration_directory) / ".nyssa-manifest-key"` (`0600`).
     - When `account != "_manifest"`, read/write `configuration_directory / ".nyssa-key"` (`0600`).
   - When `ConfigurationBootstrap` (TS) or `ConfigurationClient.create` (Python) auto-resolves a `KeyringProtectedFile` (no caller-injected keyring), pass `manifestDirectory = dirname(manifestPath)` so both app and manifest paths are bound cleanly on the single keyring instance! Also support optional `manifestKeyring` / `manifest_keyring` when callers want separate instances.
3. **`KeyringSystemdCreds` & Linux `.cred` Key Isolation (`P0-2`):**
   - Across **C#** (`KeyringSystemdCreds.cs`), **TypeScript** (`KeyringSystemdCreds.ts` and `KeyringOsPlatform.ts`), and **Python** (`keyring_systemd_creds.py`):
     - Define manifest credential filenames: `("nyssa-ai-configuration._manifest", "_manifest.key", "_manifest", "nyssa-manifest-key", "nyssa-manifest-key.key")`.
     - When `account == "_manifest"`, match **ONLY** those manifest credential filenames (plus explicit `_credentialName` if specifically constructed for manifest; bootstrap constructs `KeyringSystemdCreds()` with no per-app override when reading `_manifest`) — **NEVER** fall back to generic app names (`nyssa-key`, `master-key`, `service`) and **NEVER** use the `files.Length == 1` wildcard!
     - When `account != "_manifest"`, check `<sanitize(service)>.<sanitize(account)>` first and **filter out** all manifest credential filenames before evaluating the `files.Length == 1` single-file fallback!
4. **Linux `.cred` Existence Order (`P1-8`):**
   - In Python (`resolve_keyring`) and TypeScript (`ConfigurationBootstrap.resolveKeyring`), when `CREDENTIALS_DIRECTORY` is not active:
     1. Check if `.nyssa-key` (or `.nyssa-manifest-key` when resolving for manifest) exists -> `KeyringProtectedFile(configDir, dirname(manifestPath))`.
     2. On Linux, check if `<NYSSA_SYSTEMD_CREDS_DIR or ~/.nyssa-ai/credentials>/nyssa-ai-configuration.<sanitizedAccount>.cred` (or `._manifest.cred`) ALREADY exists -> `KeyringDefault()` / `KeyringOsPlatform()`.
     3. Check `NYSSA_CONFIG_DIR`, `/.dockerenv`, `/run/.containerenv`, or headless Linux (`!DBUS_SESSION_BUS_ADDRESS && !DISPLAY && !WAYLAND_DISPLAY`) -> `KeyringProtectedFile(configDir, dirname(manifestPath))`.
     4. Otherwise -> `KeyringDefault()` / `KeyringOsPlatform()`.
5. **Associated Data (AAD), Read-Only Unsealing & `preserveExistingSecrets` (`P0-2`, `P1-1`):**
   - AAD for `ConfigurationCipherAesGcm.seal` / `open`: `namespace = f"keyVaults/{vaultName}"`, `key = secretKey`.
   - **Bootstrap / Read:**
     - If `manifest.keyVaults` has **0** secret entries across all vaults, do **NOT** call keyring for `"_manifest"`.
     - If `manifest.keyVaults` has **1+** secret entries, read `"_manifest"` via `get` / `get_key` (read-only!). Treat **both** `Ok(None)` / `Success(null)` AND `Failure(NotFound)` as "manifest encryption key `'nyssa-ai-configuration/_manifest'` not found" and fail closed (`Validation` / `Forbidden`) without calling `set`. Zero key bytes in `finally`.
   - **Manifest Vault Write (`SetManifestKeyVault` / `setVault` / `set_vault`):**
     - Signature includes `preserveExistingSecrets = false` (`preserve_existing_secrets: bool = False`).
     - Under `<manifestPath>.lock`, read existing manifest:
       - If `preserveExistingSecrets` is `true` and `existing.keyVaults[vault.name]` exists, start with its validated on-disk `ManifestSecretEnvelope` map for any key NOT present in `vault.secrets` (without decrypting or re-encrypting!).
       - Only if `vault.secrets` has 1+ new/updated plaintext entries, call `getOrCreateKey` for `("nyssa-ai-configuration", "_manifest")`, seal those entries into `{ "value": ciphertext, "provider": "aesLocal", "encrypted": true }` with AAD `("keyVaults/" + vault.name, secretKey)`, and zero key bytes in `finally`.
       - Publish atomically (`0600` via `ProtectedFileSystem`).

### 2.3 Multi-Vault Layering & `SecretInput` Provider Labels (`P2-1`, `P2-2`)

1. **Programmatic + Manifest Multi-Vault Layering:**
   - Programmatic options accept both `keyVault` and `keyVaults` (`KeyVault` + `KeyVaults` in C#, `keyVault` + `keyVaults` in TS, `key_vault` + `key_vaults` in Python); reject duplicate programmatic vault names with `Validation`.
   - Overlay all entries in `manifest.keyVaults` by `vault.name`: a manifest entry with the same `name` as a programmatic vault replaces it whole (inheriting `codeVault.cacheTTLSeconds` only when manifest `cacheTTLSeconds` is omitted/null), while distinct vault names coexist in `ConfigurationStore`.
2. **`SecretInput` Provider Formatting (`CliSecretInputProvider` & `LoopbackSecretInputProvider` across C#, TS, Python):**
   - Match `bws`, `bitwarden`, `bitwarden-secrets`, `bws-*`, and `bitwarden-*` (case-insensitive) as Bitwarden Secrets Manager.
   - Include `[<providerName>]` when `providerName` differs from canonical generic names (`aesLocal`, `literal`) in both CLI and Loopback `<option>` labels so `bws-corporate`, `bws-prod`, and `bws-staging` are clearly distinguishable!

---

## 3. Implementation Waves & Strict File Ownership

### Wave 1 — `NyssaAI.Shared` (`/srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults`)

#### Slice S1: Python (`nyssa-ai-configuration-py`) + Canonical Specs & Docs
- **Owned Files:**
  - `nyssa-ai-configuration-py/src/nyssa_ai_configuration/manifest/{types,configuration_manifest_reader,configuration_manifest_writer}.py`
  - `nyssa-ai-configuration-py/src/nyssa_ai_configuration/keyring/{keyring,keyring_protected_file,keyring_systemd_creds}.py`
  - `nyssa-ai-configuration-py/src/nyssa_ai_configuration/secret_input/{cli_secret_input_provider,loopback_secret_input_provider}.py`
  - `nyssa-ai-configuration-py/src/nyssa_ai_configuration/{configuration_client,__init__}.py`
  - `nyssa-ai-configuration-py/test/unit/{test_manifest,test_keyring_systemd_creds,test_secret_capture}.py`
  - `nyssa-ai-configuration-py/test/use_case/{test_bitwarden_vault_tutorials,test_vault_provider,test_basic_settings}.py`
  - `docs/specs/config/{README.md,config-manifest.json,config-manifest.vault.json,platform-filesystem-layout.md}`
  - `docs/configuration/features/f-001/uc-002-verify-private-manifest-permissions.md`
  - `docs/configuration/features/f-002/{feature.md,uc-003-apply-manifest-overrides-for-timeout-and-vault.md}`
  - `docs/configuration/features/f-103/feature.md`
  - `docs/configuration/features/f-202/uc-005-sign-in-to-oauth-protected-cloud-vault.md`
  - `docs/configuration/features/f-203/{feature.md,uc-001-save-and-read-secret-in-cloud-vault.md}`

#### Slice S2: TypeScript (`nyssa-ai-configuration-ts`)
- **Owned Files:**
  - `nyssa-ai-configuration-ts/src/manifest/{ManifestSecretEnvelope,VaultManifestEntry,ConfigurationManifest,ApplicationManifestEntry,ResolvedApplicationSettings,ConfigurationManifestReader,ConfigurationManifestWriter}.ts`
  - `nyssa-ai-configuration-ts/src/keyring/{KeyringDefault,KeyringOsPlatform,KeyringProtectedFile,KeyringSystemdCreds}.ts`
  - `nyssa-ai-configuration-ts/src/secretInput/{CliSecretInputProvider,LoopbackSecretInputProvider}.ts`
  - `nyssa-ai-configuration-ts/src/{ConfigurationOptions,ConfigurationBootstrap}.ts`
  - `nyssa-ai-configuration-ts/test/unit/{ManifestReader,KeyringSystemdCreds,SecretCapture}.test.ts`
  - `nyssa-ai-configuration-ts/test/use_case/{SetupTutorials,BitwardenVault,OAuthTutorials}.usecase.test.ts`

#### Slice S3: C# (`nyssa-ai-configuration-cs`)
- **Owned Files:**
  - `nyssa-ai-configuration-cs/src/Manifest/{ManifestSecretEnvelope,VaultManifestEntry,ConfigurationManifest,ApplicationManifestEntry,ApplicationManifestSettings,ManifestJsonContext,ConfigurationManifestReader,ConfigurationManifestWriter}.cs`
  - `nyssa-ai-configuration-cs/src/OsKeyring/KeyringSystemdCreds.cs`
  - `nyssa-ai-configuration-cs/src/SecretInput/{CliSecretInputProvider,LoopbackSecretInputProvider}.cs`
  - `nyssa-ai-configuration-cs/src/Client/{BootstrapOptions,ConfigurationServiceCollectionExtensions}.cs`
  - `nyssa-ai-configuration-cs/test/unit/Manifest/{ConfigurationManifestReaderTests,ConfigurationManifestWriterTests}.cs`
  - `nyssa-ai-configuration-cs/test/unit/Client/NyssaConfigurationOptionsTests.cs`
  - `nyssa-ai-configuration-cs/test/unit/OsKeyring/KeyringTests.cs`
  - `nyssa-ai-configuration-cs/test/use_case/{SetupTutorials,BitwardenVaultTutorials,OAuthTutorials}.cs`

---

### Wave 2 — `vps-operations` (`/srv/repos/NyssaAI/vps-operations/.tmp/root-manifest-vaults`)

#### Slice S4: `nyssa-secrets` Multi-Vault CLI, Migration, Docs & Assembly
- **Owned Files:**
  - `src/cli/{secrets_cli,setup_plugin}.py`
  - `skills/vps-secrets/SKILL.md`
  - `skills/vps-operations/workflows/setup.md`
  - `test/unit/{test_secrets,test_setup}.py`
  - `.agents/packages/vps-operations/**` & `.agents/plugins/vps-operations/**` (regenerated via `python3 scripts/assemble.py write`)
- **Key Requirements (`P0-1`, `P1-1`, `P1-4`, `P1-5`, `P1-7`, `P1-8`, `P2-3`):**
  - `build_keyring(keyring_mode, target_dir, home_dir, manifest_dir, account_name)`:
    - Always pass `manifest_directory=manifest_dir` when constructing `KeyringProtectedFile`.
    - In `auto` mode:
      1. If `(target_dir / ".nyssa-key").is_file()` or `(manifest_dir / ".nyssa-manifest-key").is_file()` -> `KeyringProtectedFile(target_dir, manifest_directory=manifest_dir)`.
      2. Compute `creds_dir = Path(os.environ.get("NYSSA_SYSTEMD_CREDS_DIR") or (home_dir / ".nyssa-ai/credentials"))`. If `(creds_dir / f"nyssa-ai-configuration.{account_name}.cred").is_file()` or `(creds_dir / "nyssa-ai-configuration._manifest.cred").is_file()` -> `KeyringDefault()` (setting `NYSSA_SYSTEMD_CREDS_DIR` if `home_dir.resolve() != Path.home().resolve()`).
      3. Only select `KeyringDefault()` on `geteuid() == 0` when `home_dir.resolve() == Path.home().resolve()` (not a mock/overridden `--home`), not in a container (`/.dockerenv`, `/run/.containerenv`), `shutil.which("systemd-creds")` exists, and `/var/lib/systemd/credential.secret` (or `/run/systemd/system`) exists; otherwise fall back to `KeyringProtectedFile(target_dir, manifest_directory=manifest_dir)`.
  - Parse manifests via `ConfigurationManifestReader.read_manifest(manifest_path)` when runtime is available (enforcing JSONC support, `0600` private-file check, and legacy `applications.<app>.keyVault` rejection).
  - Subcommands:
    - `nyssa-secrets vault set <name> [--kind bitwarden-secrets] [--org-id <uuid>] [--project-id <uuid>] [--endpoint <url>] [--credential <ns/key>] [--cache-ttl 60] [--stdin] [--no-secret]` (uses `preserve_existing_secrets=True` when `--no-secret` or `--credential` is used without token input; clears `@u` cache for `args.app` AND every app in `manifest.applications`).
    - `nyssa-secrets vault list [--json]`
    - `nyssa-secrets vault remove <name>` (clears `@u` cache for `args.app` AND every app in `manifest.applications`).
    - `nyssa-secrets move <namespace> <key> <destination-provider>` (handles `ConfigurationAttempt[ConfigurationMove]` via `attempt.result`, reports `source_value_remains` / `source_delete_error` on stderr if true, exports `attempt.recovery_handle.export()` on interrupted failure, and invalidates `@u` cache for `(args.app, namespace, key)`).

---

## 4. Verification Commands

- **Python (`NyssaAI.Shared`):**
  ```bash
  cd /srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults/nyssa-ai-configuration-py && \
  PYTHONPATH=../nyssa-ai-result-py/src:../nyssa-ai-logging-py/src:src uv run --no-project --with mypy --with pytest --with cryptography mypy --config-file pyproject.toml src test && \
  PYTHONPATH=../nyssa-ai-result-py/src:../nyssa-ai-logging-py/src:src uv run --no-project --with pytest --with cryptography pytest -q && \
  python3 /srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults/.nyssa-ai/dev-standards/python/scripts/check.py /srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults/nyssa-ai-configuration-py/src
  ```
- **TypeScript (`NyssaAI.Shared`):**
  ```bash
  cd /srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults/nyssa-ai-configuration-ts && \
  npm run typecheck && npm run build && npx vitest run && \
  node /srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults/.nyssa-ai/dev-standards/typescript/scripts/check.mjs /srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults/nyssa-ai-configuration-ts/src
  ```
- **C# (`NyssaAI.Shared`):**
  - `dotnet` SDK is not installed on this Linux host (`blocked: missing prerequisite dotnet SDK`); verify static invariants (`PublicTypeInventoryTests` exported types unchanged, `PackageStructureTests` rules, XML docs, `ManifestJsonContext` AOT attributes).
- **`vps-operations`:**
  ```bash
  cd /srv/repos/NyssaAI/vps-operations/.tmp/root-manifest-vaults && \
  python3 scripts/assemble.py write && \
  python3 scripts/assemble.py check && \
  python3 scripts/validate.py && \
  NYSSA_SHARED_REPO=/srv/repos/NyssaAI/NyssaAI.Shared/.worktrees/root-manifest-vaults python3 -m unittest discover -s test/unit -v
  ```
