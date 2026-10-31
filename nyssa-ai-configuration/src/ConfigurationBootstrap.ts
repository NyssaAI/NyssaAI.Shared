// Responsibility: Bootstrap helper resolving manifest settings, protected directories, master keys, and external vault providers.
// Public interface:
// `-- ConfigurationBootstrap
//     +-- createStore(options)
//     `-- inferApplicationName()

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "./results/Result.js";
import { ConfigurationManifestReader } from "./manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "./manifest/ConfigurationManifestWriter.js";
import { KeyringDefault } from "./keyring/KeyringDefault.js";
import { KeyringOsPlatform } from "./keyring/KeyringOsPlatform.js";
import { ConfigurationCipherAesGcm } from "./encryption/ConfigurationCipherAesGcm.js";
import { ConfigurationStore } from "./values/ConfigurationStore.js";
import { ConfigurationDocumentLocalFile } from "./values/ConfigurationDocumentLocalFile.js";
import { NullConfigurationLogger } from "./logging/NullConfigurationLogger.js";
import { ProtectedFileSystem } from "./fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "./fileSecurity/DirectorySafety.js";
import type { ConfigurationOptions } from "./ConfigurationOptions.js";
import type { IVaultProvider } from "./vault/IVaultProvider.js";
import type { VaultOptions } from "./manifest/VaultOptions.js";
import type { IConfigurationLogger } from "./logging/IConfigurationLogger.js";

/**
 * ConfigurationBootstrap resolves manifest sections, verifies directory safety,
 * initializes the AES-256-GCM cipher from the keyring, and wires external vault providers.
 */
export class ConfigurationBootstrap {
  public static async createStore(
    options?: ConfigurationOptions
  ): Promise<
    Result<{
      readonly applicationName: string;
      readonly configurationDirectory: string;
      readonly store: ConfigurationStore;
      readonly logger: IConfigurationLogger;
    }>
  > {
    const resolvedOptions = options ?? {};
    const applicationName =
      resolvedOptions.applicationName && resolvedOptions.applicationName.trim().length > 0
        ? resolvedOptions.applicationName.trim()
        : this.inferApplicationName();

    if (!applicationName || applicationName.trim().length === 0) {
      return fail("applicationName must be specified.", "Validation");
    }

    const logger = resolvedOptions.logger ?? new NullConfigurationLogger();
    const manifestPath =
      resolvedOptions.configManifest && resolvedOptions.configManifest.trim().length > 0
        ? path.resolve(resolvedOptions.configManifest)
        : ConfigurationManifestReader.defaultManifestPath();

    let configDir: string;
    let lockTimeoutMs = resolvedOptions.lockTimeoutMs ?? ConfigurationManifestReader.DEFAULT_LOCK_TIMEOUT_MS;
    let effectiveVault: VaultOptions | undefined = resolvedOptions.keyVault;

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, applicationName);
    if (readRes.ok) {
      configDir = readRes.value.configurationDirectory;
      lockTimeoutMs = resolvedOptions.lockTimeoutMs ?? readRes.value.lockTimeoutMs;
      if (readRes.value.keyVault !== undefined) {
        effectiveVault = readRes.value.keyVault;
      }
    } else if (readRes.category === "NotFound") {
      const defaultDir =
        resolvedOptions.configDirectory && resolvedOptions.configDirectory.trim().length > 0
          ? path.resolve(resolvedOptions.configDirectory)
          : path.join(path.dirname(manifestPath), applicationName);

      const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
        manifestPath,
        applicationName,
        defaultDir,
        lockTimeoutMs
      );
      if (!writeRes.ok) {
        return fail(`Failed to provision manifest section: ${writeRes.error}`, writeRes.category ?? "Forbidden");
      }
      configDir = defaultDir;
    } else {
      return readRes;
    }

    const dirRes = ProtectedFileSystem.createDirectory(configDir);
    if (!dirRes.ok) {
      return dirRes;
    }

    const dirSafe = DirectorySafety.verify(configDir);
    if (!dirSafe.ok) {
      return dirSafe;
    }

    const keyring = resolvedOptions.keyring ?? new KeyringOsPlatform();
    const keyRes = await KeyringDefault.getOrCreateKey(applicationName, keyring);
    if (!keyRes.ok) {
      return keyRes;
    }

    const keyBuf = keyRes.value;
    const cipherRes = ConfigurationCipherAesGcm.create(keyBuf);
    keyBuf.fill(0);

    if (!cipherRes.ok) {
      return cipherRes;
    }

    const vaultProviders = new Map<string, IVaultProvider>();
    const vaultCredentials = new Map<string, { readonly namespace: string; readonly key: string }>();
    let storeRef: ConfigurationStore | undefined;

    if (effectiveVault !== undefined) {
      if (!effectiveVault.name || effectiveVault.name.trim().length === 0) {
        return fail("keyVault.name must not be blank.", "Validation");
      }
      if (!effectiveVault.kind || effectiveVault.kind.trim().length === 0) {
        return fail(`keyVault '${effectiveVault.name}' must specify a non-blank kind.`, "Validation");
      }

      if (effectiveVault.credential !== undefined && effectiveVault.credential.trim().length > 0) {
        const parts = effectiveVault.credential.split("/");
        if (parts.length !== 2 || !parts[0] || !parts[1]) {
          return fail(
            `keyVault.credential '${effectiveVault.credential}' must be in 'namespace/key' format.`,
            "Validation"
          );
        }
        const nsValid = ConfigurationDocumentLocalFile.validateSegment(parts[0], "namespace");
        if (!nsValid.ok) {
          return nsValid;
        }
        const keyValid = ConfigurationDocumentLocalFile.validateSegment(parts[1], "key");
        if (!keyValid.ok) {
          return keyValid;
        }
        vaultCredentials.set(effectiveVault.name, { namespace: parts[0], key: parts[1] });
      }

      const factories = resolvedOptions.vaultFactories ?? [];
      const matchingFactory = factories.find((f) => f.kind === effectiveVault.kind);
      if (!matchingFactory) {
        return fail(
          `No IVaultProviderFactory registered for vault kind '${effectiveVault.kind}'.`,
          "Validation"
        );
      }

      const normalizedVault: VaultOptions = {
        ...effectiveVault,
        settings: effectiveVault.settings ?? {},
        secrets: effectiveVault.secrets ?? {}
      };

      const vaultName = normalizedVault.name;
      const providerRes = matchingFactory.create(normalizedVault, async (sig) => {
        if (!storeRef) {
          return fail("ConfigurationStore is not yet initialized.", "Unavailable");
        }
        return storeRef.resolveVaultCredential(vaultName, sig);
      });

      if (!providerRes.ok) {
        return providerRes;
      }

      vaultProviders.set(vaultName, providerRes.value);
    }

    const store = new ConfigurationStore(configDir, lockTimeoutMs, cipherRes.value, {
      vaultProviders,
      vaultCredentials,
      oauthClient: resolvedOptions.oauthClient,
      browserLauncher: resolvedOptions.browserLauncher,
      clock: resolvedOptions.clock
    });
    storeRef = store;

    return ok({
      applicationName,
      configurationDirectory: configDir,
      store,
      logger
    });
  }

  public static inferApplicationName(): string {
    try {
      const pkgPath = path.resolve(process.cwd(), "package.json");
      if (fs.existsSync(pkgPath)) {
        const raw = fs.readFileSync(pkgPath, "utf8");
        const parsed = JSON.parse(raw) as { name?: unknown };
        if (typeof parsed.name === "string" && parsed.name.trim().length > 0) {
          const base = parsed.name.includes("/") ? parsed.name.split("/").pop()! : parsed.name;
          const sanitized = base.replace(/[^A-Za-z0-9._-]/g, "");
          if (sanitized.length > 0) {
            return sanitized;
          }
        }
      }
    } catch {
      // fall through
    }
    return "NyssaApp";
  }
}
