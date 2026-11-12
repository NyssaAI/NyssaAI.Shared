// Responsibility: Write and provision root keyVaults and application sections in the configuration manifest atomically under lock.
// Public interface:
// `-- ConfigurationManifestWriter
//     +-- DEFAULT_LOCK_TIMEOUT_MS
//     +-- removeVault(manifestPath, vaultName, lockTimeoutMs)
//     +-- setConfigurationDirectory(manifestPath, applicationName, configurationDirectory, lockTimeoutMs)
//     `-- setVault(manifestPath, vault, options)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, type Result } from "@nyssa-ai/result";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import { ConfigurationCipherAesGcm } from "../encryption/ConfigurationCipherAesGcm.js";
import { KeyringDefault } from "../keyring/KeyringDefault.js";
import { ConfigurationBootstrap } from "../ConfigurationBootstrap.js";
import { ConfigurationManifestReader } from "./ConfigurationManifestReader.js";
import { ManifestVaultValidator } from "./ManifestVaultValidator.js";
import { ManifestFileLock } from "./ManifestFileLock.js";
import type { IKeyring } from "../keyring/IKeyring.js";
import type { ConfigurationManifest } from "./ConfigurationManifest.js";
import type { ApplicationManifestEntry } from "./ApplicationManifestEntry.js";
import type { VaultManifestEntry } from "./VaultManifestEntry.js";
import type { ManifestSecretEnvelope } from "./ManifestSecretEnvelope.js";
import type { VaultOptions } from "./VaultOptions.js";

/**
 * ConfigurationManifestWriter updates configuration manifest sections atomically under a lock file.
 */
export class ConfigurationManifestWriter {
  public static readonly DEFAULT_LOCK_TIMEOUT_MS = 10000;

  /**
   * Sets or creates the configuration directory for an application in the manifest.
   */
  public static setConfigurationDirectory(
    manifestPath: string,
    applicationName: string,
    configurationDirectory: string,
    lockTimeoutMs?: number
  ): Result<string> {
    const resolvedPath = path.resolve(manifestPath);
    const parentDir = path.dirname(resolvedPath);
    const timeoutMs = lockTimeoutMs ?? this.DEFAULT_LOCK_TIMEOUT_MS;

    const dirRes = ProtectedFileSystem.createDirectory(parentDir);
    if (!dirRes.ok) return dirRes;
    const safeRes = DirectorySafety.verify(parentDir);
    if (!safeRes.ok) return safeRes;

    return ManifestFileLock.withLockSync(resolvedPath, timeoutMs, () => {
      let manifest: ConfigurationManifest = {
        version: ConfigurationManifestReader.SUPPORTED_VERSION,
        applications: {}
      };
      if (fs.existsSync(resolvedPath)) {
        const readRes = ConfigurationManifestReader.readManifest(resolvedPath);
        if (!readRes.ok) return readRes;
        manifest = readRes.value;
      }

      const apps: Record<string, ApplicationManifestEntry> = { ...(manifest.applications ?? {}) };
      const existingSection = apps[applicationName] ?? {};
      apps[applicationName] = {
        ...existingSection,
        configurationDirectory,
        ...(lockTimeoutMs !== undefined
          ? { lockTimeoutMs: Math.round(lockTimeoutMs) }
          : existingSection.lockTimeoutMs !== undefined
            ? { lockTimeoutMs: existingSection.lockTimeoutMs }
            : existingSection.lockTimeout !== undefined
              ? { lockTimeout: existingSection.lockTimeout }
              : { lockTimeoutMs: this.DEFAULT_LOCK_TIMEOUT_MS })
      };

      const writeRes = ManifestFileLock.writeJsonAtomically(
        resolvedPath,
        manifestPath,
        this.prepareForDisk({
          version: manifest.version || ConfigurationManifestReader.SUPPORTED_VERSION,
          ...(manifest.keyVaults !== undefined ? { keyVaults: manifest.keyVaults } : {}),
          applications: apps
        })
      );
      return writeRes.ok ? ok(configurationDirectory) : writeRes;
    });
  }

  /**
   * Adds or updates a root keyVaults entry, sealing any secrets with AES-256-GCM under '_manifest'.
   */
  public static async setVault(
    manifestPath: string,
    vault: VaultOptions,
    options?: {
      readonly keyring?: IKeyring | undefined;
      readonly lockTimeoutMs?: number | undefined;
      readonly preserveExistingSecrets?: boolean | undefined;
    }
  ): Promise<Result<VaultManifestEntry>> {
    const validVaultRes = ManifestVaultValidator.validateVaultOptions(vault);
    if (!validVaultRes.ok) return validVaultRes;
    const normalizedVault = validVaultRes.value;

    const resolvedPath = path.resolve(manifestPath);
    const parentDir = path.dirname(resolvedPath);
    const timeoutMs = options?.lockTimeoutMs ?? this.DEFAULT_LOCK_TIMEOUT_MS;

    const dirRes = ProtectedFileSystem.createDirectory(parentDir);
    if (!dirRes.ok) return dirRes;
    const safeRes = DirectorySafety.verify(parentDir);
    if (!safeRes.ok) return safeRes;

    if (fs.existsSync(resolvedPath)) {
      const preRead = ConfigurationManifestReader.readManifest(resolvedPath);
      if (!preRead.ok) return preRead;
    }

    const incomingSecrets = normalizedVault.secrets ?? {};
    const sealedSecrets: Record<string, ManifestSecretEnvelope> = {};
    if (Object.keys(incomingSecrets).length > 0) {
      const manifestKeyring = options?.keyring ?? ConfigurationBootstrap.resolveManifestKeyring(resolvedPath);
      const keyRes = await KeyringDefault.getOrCreateKey(KeyringDefault.MANIFEST_ACCOUNT, manifestKeyring);
      if (!keyRes.ok) return keyRes;

      const keyBuf = keyRes.value;
      try {
        const cipherRes = ConfigurationCipherAesGcm.create(keyBuf);
        if (!cipherRes.ok) return cipherRes;
        const aadNamespace = `keyVaults/${normalizedVault.name}`;
        for (const [secretKey, plaintext] of Object.entries(incomingSecrets)) {
          const sealRes = cipherRes.value.seal(plaintext, aadNamespace, secretKey);
          if (!sealRes.ok) return sealRes;
          const env: ManifestSecretEnvelope = {
            value: sealRes.value,
            provider: "aesLocal",
            encrypted: true
          };
          Object.defineProperty(env, "toString", {
            value: () => "ManifestSecretEnvelope { [REDACTED] }",
            enumerable: false
          });
          sealedSecrets[secretKey] = env;
        }
      } finally {
        keyBuf.fill(0);
      }
    }

    return ManifestFileLock.withLockSync(resolvedPath, timeoutMs, () => {
      let manifest: ConfigurationManifest = {
        version: ConfigurationManifestReader.SUPPORTED_VERSION,
        applications: {}
      };
      if (fs.existsSync(resolvedPath)) {
        const readRes = ConfigurationManifestReader.readManifest(resolvedPath);
        if (!readRes.ok) return readRes;
        manifest = readRes.value;
      }

      const mergedSecrets: Record<string, ManifestSecretEnvelope> =
        options?.preserveExistingSecrets === true
          ? { ...(manifest.keyVaults?.[normalizedVault.name]?.secrets ?? {}), ...sealedSecrets }
          : { ...sealedSecrets };

      const entry: VaultManifestEntry = {
        name: normalizedVault.name,
        kind: normalizedVault.kind,
        ...(normalizedVault.endpoint !== undefined ? { endpoint: normalizedVault.endpoint } : {}),
        ...(normalizedVault.credential !== undefined ? { credential: normalizedVault.credential } : {}),
        ...(normalizedVault.cacheTTLSeconds !== undefined ? { cacheTTLSeconds: normalizedVault.cacheTTLSeconds } : {}),
        settings: { ...(normalizedVault.settings ?? {}) },
        secrets: mergedSecrets
      };

      const updatedVaults: Record<string, VaultManifestEntry> = {
        ...(manifest.keyVaults ?? {}),
        [normalizedVault.name]: entry
      };

      const writeRes = ManifestFileLock.writeJsonAtomically(
        resolvedPath,
        manifestPath,
        this.prepareForDisk({
          version: manifest.version || ConfigurationManifestReader.SUPPORTED_VERSION,
          keyVaults: updatedVaults,
          applications: { ...(manifest.applications ?? {}) }
        })
      );
      return writeRes.ok ? ok(entry) : writeRes;
    });
  }

  /**
   * Removes a named vault from root keyVaults atomically under lock.
   */
  public static removeVault(manifestPath: string, vaultName: string, lockTimeoutMs?: number): Result<boolean> {
    const validName = ManifestVaultValidator.validateVaultName(vaultName);
    if (!validName.ok) return validName;

    const resolvedPath = path.resolve(manifestPath);
    if (!fs.existsSync(resolvedPath)) return ok(false);

    const safeRes = DirectorySafety.verify(path.dirname(resolvedPath));
    if (!safeRes.ok) return safeRes;

    const timeoutMs = lockTimeoutMs ?? this.DEFAULT_LOCK_TIMEOUT_MS;
    return ManifestFileLock.withLockSync(resolvedPath, timeoutMs, () => {
      if (!fs.existsSync(resolvedPath)) return ok(false);
      const readRes = ConfigurationManifestReader.readManifest(resolvedPath);
      if (!readRes.ok) return readRes;

      const updatedVaults: Record<string, VaultManifestEntry> = { ...(readRes.value.keyVaults ?? {}) };
      if (!Object.prototype.hasOwnProperty.call(updatedVaults, vaultName)) {
        return ok(false);
      }
      delete updatedVaults[vaultName];

      const writeRes = ManifestFileLock.writeJsonAtomically(
        resolvedPath,
        manifestPath,
        this.prepareForDisk({
          version: readRes.value.version || ConfigurationManifestReader.SUPPORTED_VERSION,
          keyVaults: updatedVaults,
          applications: { ...(readRes.value.applications ?? {}) }
        })
      );
      return writeRes.ok ? ok(true) : writeRes;
    });
  }

  private static prepareForDisk(manifest: ConfigurationManifest): Record<string, unknown> {
    const diskVaults: Record<string, Record<string, unknown>> = {};
    for (const [vaultName, entry] of Object.entries(manifest.keyVaults ?? {})) {
      const settings = entry.settings ?? {};
      const secrets = entry.secrets ?? {};
      diskVaults[vaultName] = {
        kind: entry.kind,
        ...(entry.endpoint !== undefined ? { endpoint: entry.endpoint } : {}),
        ...(entry.credential !== undefined ? { credential: entry.credential } : {}),
        ...(entry.cacheTTLSeconds !== undefined ? { cacheTTLSeconds: entry.cacheTTLSeconds } : {}),
        ...(Object.keys(settings).length > 0 ? { settings } : {}),
        ...(Object.keys(secrets).length > 0 ? { secrets } : {})
      };
    }

    return {
      version: manifest.version || ConfigurationManifestReader.SUPPORTED_VERSION,
      ...(Object.keys(diskVaults).length > 0 ? { keyVaults: diskVaults } : {}),
      applications: manifest.applications ?? {}
    };
  }
}
