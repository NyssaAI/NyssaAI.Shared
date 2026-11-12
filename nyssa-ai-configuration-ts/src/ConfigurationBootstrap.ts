// Responsibility: Bootstrap helper resolving manifest settings, protected directories, master keys, and external vault providers.
// Public interface:
// `-- ConfigurationBootstrap
//     +-- createStore(options)
//     +-- inferApplicationName()
//     +-- resolveKeyring(configurationDirectory, options)
//     `-- resolveManifestKeyring(manifestPath, options)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationManifestReader } from "./manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "./manifest/ConfigurationManifestWriter.js";
import { ManifestVaultUnsealer } from "./manifest/ManifestVaultUnsealer.js";
import { KeyringDefault } from "./keyring/KeyringDefault.js";
import { KeyringOsPlatform } from "./keyring/KeyringOsPlatform.js";
import { KeyringProtectedFile } from "./keyring/KeyringProtectedFile.js";
import { KeyringSystemdCreds } from "./keyring/KeyringSystemdCreds.js";
import { ConfigurationCipherAesGcm } from "./encryption/ConfigurationCipherAesGcm.js";
import { ConfigurationRecoveryTokenProtector } from "./recovery/ConfigurationRecoveryTokenProtector.js";
import { ConfigurationStore } from "./values/ConfigurationStore.js";
import { NullConfigurationLogger } from "./logging/NullConfigurationLogger.js";
import { ProtectedFileSystem } from "./fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "./fileSecurity/DirectorySafety.js";
import { VaultProviderFactoryBitwarden } from "./vault/bitwarden/VaultProviderFactoryBitwarden.js";
import { VaultProviderCaching } from "./vault/VaultProviderCaching.js";
import type { ConfigurationOptions } from "./ConfigurationOptions.js";
import type { IKeyring } from "./keyring/IKeyring.js";
import type { IVaultProvider } from "./vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "./vault/IVaultProviderFactory.js";
import type { VaultOptions } from "./manifest/VaultOptions.js";
import type { VaultManifestEntry } from "./manifest/VaultManifestEntry.js";
import type { OAuthOptions } from "./oauth/OAuthOptions.js";
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
      readonly keyring: IKeyring;
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

    const codeVaultsRes = ManifestVaultUnsealer.collectProgrammaticVaults(resolvedOptions);
    if (!codeVaultsRes.ok) {
      return codeVaultsRes;
    }

    const logger = resolvedOptions.logger ?? new NullConfigurationLogger();
    const manifestPath =
      resolvedOptions.configManifest && resolvedOptions.configManifest.trim().length > 0
        ? path.resolve(resolvedOptions.configManifest)
        : ConfigurationManifestReader.defaultManifestPath();

    let configDir: string;
    let lockTimeoutMs = resolvedOptions.lockTimeoutMs ?? ConfigurationManifestReader.DEFAULT_LOCK_TIMEOUT_MS;
    let manifestKeyVaults: Readonly<Record<string, VaultManifestEntry>> = {};

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, applicationName);
    if (readRes.ok) {
      configDir = readRes.value.configurationDirectory;
      lockTimeoutMs = resolvedOptions.lockTimeoutMs ?? readRes.value.lockTimeoutMs;
      manifestKeyVaults = readRes.value.keyVaults ?? {};
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
      const afterWrite = ConfigurationManifestReader.readManifest(manifestPath);
      if (!afterWrite.ok) return afterWrite;
      configDir = defaultDir;
      manifestKeyVaults = afterWrite.value.keyVaults ?? {};
    } else {
      return readRes;
    }

    const dirRes = ProtectedFileSystem.createDirectory(configDir);
    if (!dirRes.ok) return dirRes;
    const dirSafe = DirectorySafety.verify(configDir);
    if (!dirSafe.ok) return dirSafe;

    const keyring = this.resolveKeyring(configDir, {
      ...resolvedOptions,
      applicationName,
      configManifest: manifestPath
    });
    const keyRes = await KeyringDefault.getOrCreateKey(applicationName, keyring);
    if (!keyRes.ok) return keyRes;

    const keyBuf = keyRes.value;
    const cipherRes = ConfigurationCipherAesGcm.create(keyBuf);
    const recoveryProtector = new ConfigurationRecoveryTokenProtector(keyBuf, applicationName, configDir);
    keyBuf.fill(0);
    if (!cipherRes.ok) return cipherRes;

    const effectiveVaultsRes = await ManifestVaultUnsealer.resolveEffectiveVaults(
      codeVaultsRes.value,
      manifestKeyVaults,
      () =>
        resolvedOptions.manifestKeyring ??
        resolvedOptions.keyring ??
        this.resolveManifestKeyring(manifestPath, { ...resolvedOptions, configDirectory: configDir })
    );
    if (!effectiveVaultsRes.ok) return effectiveVaultsRes;

    const vaultProviders = new Map<string, IVaultProvider>();
    const vaultCredentials = new Map<string, { readonly namespace: string; readonly key: string }>();
    const vaultAuth = new Map<
      string,
      { readonly namespace: string; readonly key: string; readonly options?: OAuthOptions | undefined }
    >();
    let storeRef: ConfigurationStore | undefined;

    for (const effectiveVault of effectiveVaultsRes.value.values()) {
      if (effectiveVault.credential !== undefined && effectiveVault.credential.trim().length > 0) {
        const cleanedCred = effectiveVault.credential.startsWith("oauth://")
          ? effectiveVault.credential.slice("oauth://".length)
          : effectiveVault.credential;
        const parts = cleanedCred.split("/");
        vaultCredentials.set(effectiveVault.name, { namespace: parts[0]!, key: parts[1]! });
      }

      const factories = resolvedOptions.vaultFactories ?? [];
      let matchingFactory: IVaultProviderFactory | undefined = factories.find((f) => f.kind === effectiveVault.kind);
      if (
        !matchingFactory &&
        (effectiveVault.kind === VaultProviderFactoryBitwarden.DEFAULT_KIND ||
          effectiveVault.kind === VaultProviderFactoryBitwarden.SHORT_KIND ||
          effectiveVault.kind === VaultProviderFactoryBitwarden.LEGACY_KIND)
      ) {
        matchingFactory = new VaultProviderFactoryBitwarden(effectiveVault.kind);
      }
      if (!matchingFactory) {
        return fail(`No IVaultProviderFactory registered for vault kind '${effectiveVault.kind}'.`, "Validation");
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
      if (!providerRes.ok) return providerRes;

      const wrappedProvider = VaultProviderCaching.wrap(
        providerRes.value,
        normalizedVault,
        resolvedOptions.secretCache,
        resolvedOptions.clock
      );
      if (!wrappedProvider.ok) return wrappedProvider;

      vaultProviders.set(vaultName, wrappedProvider.value);

      const credAddress = vaultCredentials.get(vaultName);
      if (credAddress !== undefined) {
        let authOpts: OAuthOptions | undefined;
        if (matchingFactory.authorization) {
          const authRes = matchingFactory.authorization(normalizedVault);
          if (!authRes.ok) return authRes;
          authOpts = authRes.value;
        }
        vaultAuth.set(vaultName, {
          namespace: credAddress.namespace,
          key: credAddress.key,
          ...(authOpts !== undefined ? { options: authOpts } : {})
        });
      }
    }

    const store = new ConfigurationStore(configDir, lockTimeoutMs, cipherRes.value, {
      vaultProviders,
      vaultCredentials,
      vaultAuth,
      oauthClient: resolvedOptions.oauthClient,
      browserLauncher: resolvedOptions.browserLauncher,
      clock: resolvedOptions.clock,
      recoveryProtector
    });
    storeRef = store;

    return ok({
      applicationName,
      configurationDirectory: configDir,
      store,
      logger,
      keyring
    });
  }

  public static resolveKeyring(configurationDirectory: string, options?: ConfigurationOptions): IKeyring {
    if (options?.keyring !== undefined) return options.keyring;
    if (KeyringSystemdCreds.isAvailable()) return new KeyringSystemdCreds(options?.systemdCredentialName);

    const manifestDir =
      options?.configManifest && options.configManifest.trim().length > 0
        ? path.dirname(path.resolve(options.configManifest))
        : undefined;

    if (fs.existsSync(path.join(configurationDirectory, KeyringProtectedFile.KEY_FILENAME))) {
      return new KeyringProtectedFile(configurationDirectory, manifestDir);
    }

    if (process.platform === "linux") {
      const accountName =
        options?.applicationName && options.applicationName.trim().length > 0
          ? options.applicationName.trim()
          : this.inferApplicationName();
      if (KeyringOsPlatform.hasLinuxCredentialFile(KeyringDefault.SERVICE_NAME, accountName)) {
        return new KeyringOsPlatform();
      }
    }

    if (this.isHeadlessOrContainerEnvironment()) {
      return new KeyringProtectedFile(configurationDirectory, manifestDir);
    }

    return new KeyringOsPlatform();
  }

  public static resolveManifestKeyring(manifestPath: string, options?: ConfigurationOptions): IKeyring {
    if (options?.manifestKeyring !== undefined) return options.manifestKeyring;
    if (options?.keyring !== undefined) return options.keyring;
    if (KeyringSystemdCreds.isAvailable()) return new KeyringSystemdCreds();

    const manifestDir = path.dirname(path.resolve(manifestPath));
    const configDir =
      options?.configDirectory && options.configDirectory.trim().length > 0
        ? path.resolve(options.configDirectory)
        : manifestDir;

    if (fs.existsSync(path.join(manifestDir, KeyringProtectedFile.MANIFEST_KEY_FILENAME))) {
      return new KeyringProtectedFile(configDir, manifestDir);
    }

    if (
      process.platform === "linux" &&
      KeyringOsPlatform.hasLinuxCredentialFile(KeyringDefault.SERVICE_NAME, KeyringDefault.MANIFEST_ACCOUNT)
    ) {
      return new KeyringOsPlatform();
    }

    if (this.isHeadlessOrContainerEnvironment()) {
      return new KeyringProtectedFile(configDir, manifestDir);
    }

    return new KeyringOsPlatform();
  }

  public static inferApplicationName(): string {
    try {
      const pkgPath = path.resolve(process.cwd(), "package.json");
      if (fs.existsSync(pkgPath)) {
        const parsed = JSON.parse(fs.readFileSync(pkgPath, "utf8")) as { name?: unknown };
        if (typeof parsed.name === "string" && parsed.name.trim().length > 0) {
          const base = parsed.name.includes("/") ? parsed.name.split("/").pop()! : parsed.name;
          const sanitized = base.replace(/[^A-Za-z0-9._-]/g, "");
          if (sanitized.length > 0) return sanitized;
        }
      }
    } catch {
      // fall through
    }
    return "NyssaApp";
  }

  private static isHeadlessOrContainerEnvironment(): boolean {
    const envConfigDir = process.env["NYSSA_CONFIG_DIR"];
    if (envConfigDir && envConfigDir.trim().length > 0) return true;
    if (fs.existsSync("/.dockerenv") || fs.existsSync("/run/.containerenv")) return true;
    return (
      process.platform === "linux" &&
      !process.env["DBUS_SESSION_BUS_ADDRESS"] &&
      !process.env["DISPLAY"] &&
      !process.env["WAYLAND_DISPLAY"]
    );
  }
}
