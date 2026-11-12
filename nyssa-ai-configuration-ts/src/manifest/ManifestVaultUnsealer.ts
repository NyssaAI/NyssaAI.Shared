// Responsibility: Unseal encrypted root manifest vault secrets and layer manifest vaults over programmatic VaultOptions.
// Public interface:
// `-- ManifestVaultUnsealer
//     +-- collectProgrammaticVaults(options)
//     `-- resolveEffectiveVaults(codeVaults, manifestKeyVaults, resolveManifestKeyring)

import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationCipherAesGcm } from "../encryption/ConfigurationCipherAesGcm.js";
import { KeyringDefault } from "../keyring/KeyringDefault.js";
import { ManifestVaultValidator } from "./ManifestVaultValidator.js";
import type { ConfigurationOptions } from "../ConfigurationOptions.js";
import type { IKeyring } from "../keyring/IKeyring.js";
import type { VaultManifestEntry } from "./VaultManifestEntry.js";
import type { VaultOptions } from "./VaultOptions.js";

/**
 * ManifestVaultUnsealer validates programmatic VaultOptions, opens encrypted manifest secret envelopes
 * using the read-only '_manifest' key, and layers manifest vaults over code-supplied defaults by name.
 */
export class ManifestVaultUnsealer {
  public static collectProgrammaticVaults(options: ConfigurationOptions): Result<Map<string, VaultOptions>> {
    const list: VaultOptions[] = [];
    if (options.keyVault !== undefined) {
      list.push(options.keyVault);
    }
    if (options.keyVaults !== undefined) {
      list.push(...options.keyVaults);
    }

    const byName = new Map<string, VaultOptions>();
    for (const v of list) {
      const validRes = ManifestVaultValidator.validateVaultOptions(v);
      if (!validRes.ok) {
        return validRes;
      }
      if (byName.has(v.name)) {
        return fail(`Duplicate programmatic keyVault name '${v.name}'.`, "Validation");
      }
      byName.set(v.name, validRes.value);
    }
    return ok(byName);
  }

  public static async resolveEffectiveVaults(
    codeVaults: ReadonlyMap<string, VaultOptions>,
    manifestKeyVaults: Readonly<Record<string, VaultManifestEntry>>,
    resolveManifestKeyring: () => IKeyring
  ): Promise<Result<Map<string, VaultOptions>>> {
    const decryptedRes = await this.decryptManifestVaults(manifestKeyVaults, resolveManifestKeyring);
    if (!decryptedRes.ok) {
      return decryptedRes;
    }

    const effective = new Map<string, VaultOptions>(codeVaults);
    for (const [vaultName, manifestVault] of decryptedRes.value.entries()) {
      const codeVault = codeVaults.get(vaultName);
      effective.set(
        vaultName,
        codeVault?.cacheTTLSeconds !== undefined && manifestVault.cacheTTLSeconds === undefined
          ? { ...manifestVault, cacheTTLSeconds: codeVault.cacheTTLSeconds }
          : manifestVault
      );
    }

    return ok(effective);
  }

  private static async decryptManifestVaults(
    manifestKeyVaults: Readonly<Record<string, VaultManifestEntry>>,
    resolveManifestKeyring: () => IKeyring
  ): Promise<Result<Map<string, VaultOptions>>> {
    let totalSecrets = 0;
    for (const entry of Object.values(manifestKeyVaults)) {
      totalSecrets += Object.keys(entry.secrets ?? {}).length;
    }

    const decrypted = new Map<string, VaultOptions>();
    if (totalSecrets === 0) {
      for (const [vaultName, entry] of Object.entries(manifestKeyVaults)) {
        decrypted.set(vaultName, {
          name: vaultName,
          kind: entry.kind,
          ...(entry.endpoint !== undefined ? { endpoint: entry.endpoint } : {}),
          ...(entry.credential !== undefined ? { credential: entry.credential } : {}),
          ...(entry.cacheTTLSeconds !== undefined ? { cacheTTLSeconds: entry.cacheTTLSeconds } : {}),
          settings: { ...(entry.settings ?? {}) },
          secrets: {}
        });
      }
      return ok(decrypted);
    }

    const manifestKeyring = resolveManifestKeyring();
    const mKeyRes = await manifestKeyring.get(KeyringDefault.SERVICE_NAME, KeyringDefault.MANIFEST_ACCOUNT);
    if (!mKeyRes.ok) {
      if (mKeyRes.category === "NotFound") {
        return fail(
          "configuration: manifest encryption key 'nyssa-ai-configuration/_manifest' was not found in the keyring.",
          "Validation"
        );
      }
      return fail(`Failed to read manifest encryption key: ${mKeyRes.error}`, mKeyRes.category ?? "Unavailable");
    }
    if (mKeyRes.value === null) {
      return fail(
        "configuration: manifest encryption key 'nyssa-ai-configuration/_manifest' was not found in the keyring.",
        "Validation"
      );
    }
    if (mKeyRes.value.length !== KeyringDefault.KEY_LENGTH) {
      mKeyRes.value.fill(0);
      return fail(
        `keyring: '${KeyringDefault.SERVICE_NAME}/${KeyringDefault.MANIFEST_ACCOUNT}' does not hold ${KeyringDefault.KEY_LENGTH} bytes of key material`,
        "Validation"
      );
    }

    const manifestKeyBuf = Buffer.from(mKeyRes.value);
    mKeyRes.value.fill(0);
    try {
      const cipherRes = ConfigurationCipherAesGcm.create(manifestKeyBuf);
      if (!cipherRes.ok) {
        return cipherRes;
      }
      for (const [vaultName, entry] of Object.entries(manifestKeyVaults)) {
        const plainSecrets: Record<string, string> = {};
        const aadNamespace = `keyVaults/${vaultName}`;
        for (const [secretKey, env] of Object.entries(entry.secrets ?? {})) {
          const openRes = cipherRes.value.open(env.value, aadNamespace, secretKey);
          if (!openRes.ok) {
            return fail(
              `manifest: failed to decrypt keyVaults.${vaultName}.secrets.${secretKey}: ${openRes.error}`,
              openRes.category ?? "Validation"
            );
          }
          plainSecrets[secretKey] = openRes.value;
        }
        decrypted.set(vaultName, {
          name: vaultName,
          kind: entry.kind,
          ...(entry.endpoint !== undefined ? { endpoint: entry.endpoint } : {}),
          ...(entry.credential !== undefined ? { credential: entry.credential } : {}),
          ...(entry.cacheTTLSeconds !== undefined ? { cacheTTLSeconds: entry.cacheTTLSeconds } : {}),
          settings: { ...(entry.settings ?? {}) },
          secrets: plainSecrets
        });
      }
      return ok(decrypted);
    } finally {
      manifestKeyBuf.fill(0);
    }
  }
}
