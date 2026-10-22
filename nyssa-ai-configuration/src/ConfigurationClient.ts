// Responsibility: Primary entrypoint orchestrating manifest resolution, keyring custody, vault factories, and configuration store operations.
// Public interface:
// `-- ConfigurationClient
//     +-- applicationName
//     +-- authorize(namespace, key, options, signal)
//     +-- configurationDirectory
//     +-- get(namespace, key, signal)
//     +-- getAs(namespace, key, signal)
//     +-- getNamespace(namespace, signal)
//     +-- initialize(options)
//     +-- list(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- move(namespace, key, destinationProvider, signal)
//     +-- recoverWrite(handle, signal)
//     +-- remove(namespace, key, signal)
//     +-- set(namespace, key, value, options, signal)
//     +-- setAs(namespace, key, value, options, signal)
//     +-- setDocumentWriteInterceptor(interceptor)
//     `-- setNamespace(namespace, value, signal)

import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "./results/Result.js";
import { ConfigurationManifestReader } from "./manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "./manifest/ConfigurationManifestWriter.js";
import { KeyringDefault } from "./keyring/KeyringDefault.js";
import { KeyringProtectedFile } from "./keyring/KeyringProtectedFile.js";
import { ConfigurationCipherAesGcm } from "./encryption/ConfigurationCipherAesGcm.js";
import { ConfigurationStore } from "./values/ConfigurationStore.js";
import { ConfigurationDocumentLocalFile } from "./values/ConfigurationDocumentLocalFile.js";
import { NullConfigurationLogger } from "./logging/NullConfigurationLogger.js";
import { ProtectedFileSystem } from "./fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "./fileSecurity/DirectorySafety.js";
import type { IConfiguration } from "./IConfiguration.js";
import type { SetOptions } from "./SetOptions.js";
import type { ConfigurationOptions } from "./ConfigurationOptions.js";
import type { ConfigurationRemoval } from "./values/ConfigurationRemoval.js";
import type { ConfigurationMove } from "./values/ConfigurationMove.js";
import type { OAuthOptions } from "./oauth/OAuthOptions.js";
import type { ConfigurationWriteRecoveryHandle } from "./recovery/ConfigurationWriteRecoveryHandle.js";
import type { IVaultProvider } from "./vault/IVaultProvider.js";
import type { VaultOptions } from "./manifest/VaultOptions.js";
import type { IConfigurationLogger } from "./logging/IConfigurationLogger.js";
import type { LogRecord } from "./logging/LogRecord.js";

/**
 * ConfigurationClient orchestrates manifest resolution, master key custody, external vaults,
 * OAuth 2.0 PKCE credentials, and cross-process namespace documents.
 */
export class ConfigurationClient {
  private static readonly CLIENT_VERSION = "0.2.0";

  public readonly applicationName: string;
  public readonly configurationDirectory: string;
  private readonly _store: ConfigurationStore;
  private readonly _logger: IConfigurationLogger;

  private constructor(
    applicationName: string,
    configurationDirectory: string,
    store: ConfigurationStore,
    logger: IConfigurationLogger
  ) {
    this.applicationName = applicationName;
    this.configurationDirectory = configurationDirectory;
    this._store = store;
    this._logger = logger;
  }

  /**
   * Initializes a ConfigurationClient according to the supplied or default options.
   */
  public static async initialize(options?: ConfigurationOptions): Promise<Result<ConfigurationClient>> {
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

    // 1. Resolve or provision manifest section
    let configDir: string;
    let lockTimeoutMs = resolvedOptions.lockTimeoutMs ?? ConfigurationManifestReader.DEFAULT_LOCK_TIMEOUT_MS;
    let effectiveVault: VaultOptions | undefined = resolvedOptions.keyVault;

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, applicationName);
    if (readRes.ok) {
      configDir = readRes.value.configurationDirectory;
      lockTimeoutMs = resolvedOptions.lockTimeoutMs ?? readRes.value.lockTimeoutMs;
      if (readRes.value.keyVault !== undefined) {
        // Manifest keyVault replaces code-level keyVault whole
        effectiveVault = readRes.value.keyVault;
      }
    } else if (readRes.category === "NotFound") {
      // First use: provision manifest section
      const defaultDir =
        resolvedOptions.configDirectory && resolvedOptions.configDirectory.trim().length > 0
          ? path.resolve(resolvedOptions.configDirectory)
          : path.join(path.dirname(manifestPath), applicationName);

      const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
        manifestPath,
        applicationName,
        defaultDir,
        resolvedOptions.lockTimeoutMs
      );
      if (!writeRes.ok) {
        return fail(`Failed to provision manifest section: ${writeRes.error}`, writeRes.category ?? "Forbidden");
      }
      configDir = defaultDir;
    } else {
      return readRes;
    }

    // 2. Ensure protected configuration directory exists and is safe
    const dirRes = ProtectedFileSystem.createDirectory(configDir);
    if (!dirRes.ok) {
      return dirRes;
    }

    const dirSafe = DirectorySafety.verify(configDir);
    if (!dirSafe.ok) {
      return dirSafe;
    }

    // 3. Resolve master key via Keyring
    const keyring = resolvedOptions.keyring ?? new KeyringProtectedFile(configDir);
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

    // 4. Validate and wire optional external keyVault provider
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

    const client = new ConfigurationClient(applicationName, configDir, store, logger);
    return ok(client);
  }

  /**
   * Registers an optional write interceptor for simulating disk write failures in tests.
   */
  public setDocumentWriteInterceptor(interceptor?: (namespace: string) => Result<void>): void {
    this._store.setDocumentWriteInterceptor(interceptor);
  }

  public async get(namespace: string, key: string, signal?: AbortSignal): Promise<Result<string>> {
    return this.executeLogged("get", async () => {
      const res = await this._store.get(namespace, key, signal);
      if (!res.ok) {
        return res;
      }
      if (typeof res.value !== "string") {
        return ok(JSON.stringify(res.value));
      }
      return ok(res.value);
    });
  }

  public async getAs<T>(namespace: string, key: string, signal?: AbortSignal): Promise<Result<T>> {
    return this.executeLogged("getAs", async () => {
      const res = await this._store.get(namespace, key, signal);
      if (!res.ok) {
        return res;
      }
      if (res.value === null) {
        return fail(`The configuration value at '${namespace}/${key}' deserialized to null.`, "Validation");
      }
      return ok(res.value as T);
    });
  }

  public async getNamespace<T>(namespace: string, signal?: AbortSignal): Promise<Result<T>> {
    return this.executeLogged("getNamespace", async () => {
      return this._store.getNamespace<T>(namespace, signal);
    });
  }

  public async set(
    namespace: string,
    key: string,
    value: string,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    return this.executeLogged("set", async () => {
      return this._store.set(
        namespace,
        key,
        value,
        options?.encrypted,
        options?.provider,
        signal
      );
    });
  }

  public async setAs<T>(
    namespace: string,
    key: string,
    value: T,
    options?: SetOptions,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    return this.executeLogged("setAs", async () => {
      return this._store.set(
        namespace,
        key,
        value,
        options?.encrypted,
        options?.provider,
        signal
      );
    });
  }

  public async setNamespace<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>> {
    return this.executeLogged("setNamespace", async () => {
      return this._store.setNamespace<T>(namespace, value, signal);
    });
  }

  public async mergeSettings<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>> {
    return this.executeLogged("mergeSettings", async () => {
      return this._store.mergeSettings<T>(namespace, value, signal);
    });
  }

  public async list(namespace: string, signal?: AbortSignal): Promise<Result<readonly string[]>> {
    return this.executeLogged("list", async () => {
      return this._store.list(namespace, signal);
    });
  }

  public async remove(
    namespace: string,
    key: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationRemoval>> {
    return this.executeLogged("remove", async () => {
      return this._store.remove(namespace, key, signal);
    });
  }

  public async move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>> {
    return this.executeLogged("move", async () => {
      return this._store.move(namespace, key, destinationProvider, signal);
    });
  }

  public async authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    return this.executeLogged("authorize", async () => {
      return this._store.authorize(namespace, key, options, signal);
    });
  }

  public async recoverWrite(
    handle: ConfigurationWriteRecoveryHandle,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    return this.executeLogged("recoverWrite", async () => {
      return this._store.recoverWrite(handle, signal);
    });
  }

  private static inferApplicationName(): string {
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

  private async executeLogged<T>(operation: string, action: () => Promise<Result<T>>): Promise<Result<T>> {
    const start = performance.now();
    const correlationId = crypto.randomUUID();

    try {
      const result = await action();
      const durationMs = Math.round(performance.now() - start);

      const level: LogRecord["level"] = result.ok ? "info" : "warn";
      this.safeLog({
        timestamp: new Date().toISOString(),
        level,
        event: `configuration.${operation}.${result.ok ? "completed" : "failed"}`,
        service: this.applicationName,
        version: ConfigurationClient.CLIENT_VERSION,
        operation,
        correlationId,
        durationMs,
        ...(result.ok ? {} : { error: result.error })
      });

      return result;
    } catch (err) {
      const durationMs = Math.round(performance.now() - start);
      const message = err instanceof Error ? err.message : String(err);

      this.safeLog({
        timestamp: new Date().toISOString(),
        level: "error",
        event: `configuration.${operation}.unexpected_failure`,
        service: this.applicationName,
        version: ConfigurationClient.CLIENT_VERSION,
        operation,
        correlationId,
        durationMs,
        error: message
      });

      throw err;
    }
  }

  private safeLog(record: LogRecord): void {
    try {
      this._logger.log(record);
    } catch {
      // Sink failures must never change business results or throw
    }
  }
}

const _typeCheck: IConfiguration = null as unknown as ConfigurationClient;
void _typeCheck;
