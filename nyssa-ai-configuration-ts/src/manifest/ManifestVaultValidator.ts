// Responsibility: Validate root keyVaults entries, programmatic VaultOptions, and AES-256-GCM secret envelopes.
// Public interface:
// `-- ManifestVaultValidator
//     +-- hasAnySecrets(root)
//     +-- validateManifestEntry(vaultName, rawVault)
//     +-- validateVaultName(vaultName)
//     `-- validateVaultOptions(vault)

import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationCipherAesGcm } from "../encryption/ConfigurationCipherAesGcm.js";
import { ConfigurationDocumentLocalFile } from "../values/ConfigurationDocumentLocalFile.js";
import type { VaultManifestEntry } from "./VaultManifestEntry.js";
import type { ManifestSecretEnvelope } from "./ManifestSecretEnvelope.js";
import type { VaultOptions } from "./VaultOptions.js";

/**
 * ManifestVaultValidator enforces cross-language naming, URI, credential, TTL, and encrypted-envelope invariants.
 */
export class ManifestVaultValidator {
  private static readonly RESERVED_VAULT_NAMES = new Set(["aeslocal", "literal", "oauth", "default", "plaintext"]);

  public static hasAnySecrets(root: Record<string, unknown>): boolean {
    const kv = root["keyVaults"];
    if (kv && typeof kv === "object" && !Array.isArray(kv)) {
      for (const entry of Object.values(kv as Record<string, unknown>)) {
        if (this.hasNonEmptySecretsProp(entry)) {
          return true;
        }
      }
    }
    const apps = root["applications"];
    if (apps && typeof apps === "object" && !Array.isArray(apps)) {
      for (const app of Object.values(apps as Record<string, unknown>)) {
        if (app && typeof app === "object" && !Array.isArray(app)) {
          if (this.hasNonEmptySecretsProp((app as Record<string, unknown>)["keyVault"])) {
            return true;
          }
        }
      }
    }
    return false;
  }

  public static validateVaultName(vaultName: string): Result<void> {
    if (
      typeof vaultName !== "string" ||
      vaultName.trim().length === 0 ||
      vaultName.trim() !== vaultName ||
      /[\s/\\\x00-\x1F\x7F]/.test(vaultName)
    ) {
      return fail(`manifest: vault name '${vaultName}' is invalid`, "Validation");
    }
    if (this.RESERVED_VAULT_NAMES.has(vaultName.toLowerCase())) {
      return fail(`manifest: vault name '${vaultName}' is reserved`, "Validation");
    }
    return ok(undefined);
  }

  public static validateVaultOptions(vault: VaultOptions): Result<VaultOptions> {
    const nameRes = this.validateVaultName(vault.name);
    if (!nameRes.ok) {
      return nameRes;
    }
    const commonRes = this.validateCommonFields(vault.name, vault as unknown as Record<string, unknown>);
    if (!commonRes.ok) {
      return commonRes;
    }
    const normalizedSecrets: Record<string, string> = {};
    if (vault.secrets !== undefined && vault.secrets !== null) {
      if (typeof vault.secrets !== "object" || Array.isArray(vault.secrets)) {
        return fail(`manifest: keyVaults.${vault.name}.secrets must be an object`, "Validation");
      }
      for (const [secretKey, secretVal] of Object.entries(vault.secrets as Record<string, unknown>)) {
        if (!secretKey || secretKey.trim().length === 0 || /[\x00-\x1F\x7F]/.test(secretKey)) {
          return fail(`manifest: keyVaults.${vault.name}.secrets contains an invalid key`, "Validation");
        }
        if (typeof secretVal !== "string") {
          return fail(`manifest: keyVaults.${vault.name}.secrets.${secretKey} must be a string`, "Validation");
        }
        normalizedSecrets[secretKey] = secretVal;
      }
    }
    const c = commonRes.value;
    return ok({
      name: vault.name,
      kind: c.kind,
      ...(c.endpoint !== undefined ? { endpoint: c.endpoint } : {}),
      ...(c.credential !== undefined ? { credential: c.credential } : {}),
      ...(c.cacheTTLSeconds !== undefined ? { cacheTTLSeconds: c.cacheTTLSeconds } : {}),
      settings: c.settings,
      secrets: normalizedSecrets
    });
  }

  public static validateManifestEntry(vaultName: string, rawVault: unknown): Result<VaultManifestEntry> {
    const nameRes = this.validateVaultName(vaultName);
    if (!nameRes.ok) return nameRes;
    if (!rawVault || typeof rawVault !== "object" || Array.isArray(rawVault)) {
      return fail(`manifest: keyVaults.${vaultName} must be an object`, "Validation");
    }
    const raw = rawVault as Record<string, unknown>;
    if (raw["name"] !== undefined && raw["name"] !== null && raw["name"] !== vaultName) {
      return fail(`manifest: keyVaults.${vaultName}.name must match key '${vaultName}'`, "Validation");
    }
    const commonRes = this.validateCommonFields(vaultName, raw);
    if (!commonRes.ok) return commonRes;

    const normalizedSecrets: Record<string, ManifestSecretEnvelope> = {};
    if (raw["secrets"] !== undefined && raw["secrets"] !== null) {
      if (typeof raw["secrets"] !== "object" || Array.isArray(raw["secrets"])) {
        return fail(`manifest: keyVaults.${vaultName}.secrets must be an object`, "Validation");
      }
      for (const [secretKey, rawEnv] of Object.entries(raw["secrets"] as Record<string, unknown>)) {
        if (!secretKey || secretKey.trim().length === 0 || /[\x00-\x1F\x7F]/.test(secretKey)) {
          return fail(`manifest: keyVaults.${vaultName}.secrets contains an invalid key`, "Validation");
        }
        const envRes = this.validateSecretEnvelope(vaultName, secretKey, rawEnv);
        if (!envRes.ok) return envRes;
        normalizedSecrets[secretKey] = envRes.value;
      }
    }

    const c = commonRes.value;
    return ok({
      name: vaultName,
      kind: c.kind,
      ...(c.endpoint !== undefined ? { endpoint: c.endpoint } : {}),
      ...(c.credential !== undefined ? { credential: c.credential } : {}),
      ...(c.cacheTTLSeconds !== undefined ? { cacheTTLSeconds: c.cacheTTLSeconds } : {}),
      settings: c.settings,
      secrets: normalizedSecrets
    });
  }

  private static hasNonEmptySecretsProp(obj: unknown): boolean {
    if (!obj || typeof obj !== "object" || Array.isArray(obj)) {
      return false;
    }
    const secrets = (obj as Record<string, unknown>)["secrets"];
    if (secrets === undefined || secrets === null) {
      return false;
    }
    if (typeof secrets !== "object" || Array.isArray(secrets)) {
      return true;
    }
    return Object.keys(secrets).length > 0;
  }

  private static validateCommonFields(
    vaultName: string,
    raw: Record<string, unknown>
  ): Result<{
    readonly kind: string;
    readonly endpoint?: string | undefined;
    readonly credential?: string | undefined;
    readonly cacheTTLSeconds?: number | undefined;
    readonly settings: Readonly<Record<string, string>>;
  }> {
    const kind = raw["kind"];
    if (typeof kind !== "string" || kind.trim().length === 0 || /[\x00-\x1F\x7F]/.test(kind)) {
      return fail(`manifest: keyVaults.${vaultName}.kind must be a non-blank string`, "Validation");
    }

    let endpoint: string | undefined;
    if (raw["endpoint"] !== undefined && raw["endpoint"] !== null) {
      if (
        typeof raw["endpoint"] !== "string" ||
        raw["endpoint"].trim().length === 0 ||
        /[\x00-\x1F\x7F]/.test(raw["endpoint"])
      ) {
        return fail(`manifest: keyVaults.${vaultName}.endpoint must be a valid absolute URL`, "Validation");
      }
      try {
        const parsedUrl = new URL(raw["endpoint"]);
        if (!parsedUrl.protocol || !parsedUrl.host) {
          return fail(`manifest: keyVaults.${vaultName}.endpoint must be a valid absolute URL`, "Validation");
        }
        endpoint = raw["endpoint"];
      } catch {
        return fail(`manifest: keyVaults.${vaultName}.endpoint must be a valid absolute URL`, "Validation");
      }
    }

    let credential: string | undefined;
    if (raw["credential"] !== undefined && raw["credential"] !== null) {
      if (typeof raw["credential"] !== "string" || raw["credential"].trim().length === 0) {
        return fail(`manifest: keyVaults.${vaultName}.credential must be in 'namespace/key' format`, "Validation");
      }
      const cleaned = raw["credential"].startsWith("oauth://")
        ? raw["credential"].slice("oauth://".length)
        : raw["credential"];
      const parts = cleaned.split("/");
      if (parts.length !== 2 || !parts[0] || !parts[1]) {
        return fail(`manifest: keyVaults.${vaultName}.credential must be in 'namespace/key' format`, "Validation");
      }
      const nsRes = ConfigurationDocumentLocalFile.validateSegment(parts[0], "namespace");
      if (!nsRes.ok) return nsRes;
      const keyRes = ConfigurationDocumentLocalFile.validateSegment(parts[1], "key");
      if (!keyRes.ok) return keyRes;
      credential = raw["credential"];
    }

    let cacheTTLSeconds: number | undefined;
    if (raw["cacheTTLSeconds"] !== undefined && raw["cacheTTLSeconds"] !== null) {
      const ttl = raw["cacheTTLSeconds"];
      if (typeof ttl !== "number" || !Number.isInteger(ttl) || ttl < 0) {
        return fail(`manifest: keyVaults.${vaultName}.cacheTTLSeconds must be a non-negative integer`, "Validation");
      }
      cacheTTLSeconds = ttl;
    }

    const normalizedSettings: Record<string, string> = {};
    if (raw["settings"] !== undefined && raw["settings"] !== null) {
      if (typeof raw["settings"] !== "object" || Array.isArray(raw["settings"])) {
        return fail(`manifest: keyVaults.${vaultName}.settings must be an object`, "Validation");
      }
      for (const [k, v] of Object.entries(raw["settings"] as Record<string, unknown>)) {
        if (!k || k.trim().length === 0 || typeof v !== "string") {
          return fail(`manifest: keyVaults.${vaultName}.settings must map non-empty keys to strings`, "Validation");
        }
        normalizedSettings[k] = v;
      }
      const settingTtl = normalizedSettings["cacheTTLSeconds"];
      if (settingTtl !== undefined && !/^\d+$/.test(settingTtl.trim())) {
        return fail(
          `manifest: keyVaults.${vaultName}.settings['cacheTTLSeconds'] must be a non-negative integer`,
          "Validation"
        );
      }
    }

    return ok({
      kind,
      ...(endpoint !== undefined ? { endpoint } : {}),
      ...(credential !== undefined ? { credential } : {}),
      ...(cacheTTLSeconds !== undefined ? { cacheTTLSeconds } : {}),
      settings: normalizedSettings
    });
  }

  private static validateSecretEnvelope(
    vaultName: string,
    secretKey: string,
    rawEnv: unknown
  ): Result<ManifestSecretEnvelope> {
    const errMsg = `manifest: keyVaults.${vaultName}.secrets.${secretKey} must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json`;
    if (!rawEnv || typeof rawEnv !== "object" || Array.isArray(rawEnv)) {
      return fail(errMsg, "Validation");
    }
    const env = rawEnv as Record<string, unknown>;
    if (env["encrypted"] !== true || (env["provider"] !== undefined && env["provider"] !== "aesLocal")) {
      return fail(errMsg, "Validation");
    }
    const val = env["value"];
    if (typeof val !== "string" || !/^[A-Za-z0-9_-]+$/.test(val) || val.length % 4 === 1) {
      return fail(errMsg, "Validation");
    }
    let b64 = val.replace(/-/g, "+").replace(/_/g, "/");
    while (b64.length % 4 !== 0) b64 += "=";
    const decoded = Buffer.from(b64, "base64");
    if (
      decoded.length < ConfigurationCipherAesGcm.HEADER_LENGTH ||
      decoded[0] !== ConfigurationCipherAesGcm.FORMAT_VERSION
    ) {
      return fail(errMsg, "Validation");
    }
    const normalized: ManifestSecretEnvelope = {
      value: val,
      encrypted: true,
      ...(env["provider"] === "aesLocal" ? { provider: "aesLocal" } : {})
    };
    Object.defineProperty(normalized, "toString", {
      value: () => "ManifestSecretEnvelope { [REDACTED] }",
      enumerable: false
    });
    Object.defineProperty(normalized, Symbol.for("nodejs.util.inspect.custom"), {
      value: () => "ManifestSecretEnvelope { [REDACTED] }",
      enumerable: false
    });
    return ok(normalized);
  }
}
