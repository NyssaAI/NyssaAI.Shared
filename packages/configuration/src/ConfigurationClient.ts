// Responsibility: Primary entrypoint orchestrating manifest resolution, keyring custody, and configuration store operations.
// Public interface:
// +-- ConfigurationClient
// |   +-- get(namespace, key, signal?)
// |   +-- getAs<T>(namespace, key, signal?)
// |   +-- initialize(options)
// |   +-- remove(namespace, key, signal?)
// |   +-- set(namespace, key, value, options?, signal?)
// |   `-- setAs<T>(namespace, key, value, options?, signal?)

import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "./results/Result.js";
import { ConfigurationManifestReader } from "./manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "./manifest/ConfigurationManifestWriter.js";
import { KeyringDefault } from "./keyring/KeyringDefault.js";
import { KeyringProtectedFile } from "./keyring/KeyringProtectedFile.js";
import { ConfigurationCipherAesGcm } from "./encryption/ConfigurationCipherAesGcm.js";
import { ConfigurationStore } from "./values/ConfigurationStore.js";
import { NullConfigurationLogger } from "./logging/NullConfigurationLogger.js";
import { ProtectedFileSystem } from "./fileSecurity/ProtectedFileSystem.js";
import type { IConfiguration, SetOptions } from "./IConfiguration.js";
import type { ConfigurationOptions } from "./ConfigurationOptions.js";
import type { IConfigurationLogger, LogRecord, LogLevel } from "./logging/IConfigurationLogger.js";

/**
 * ConfigurationClient implements IConfiguration by orchestrating manifest resolution,
 * master key custody, and cross-process namespace documents.
 */
export class ConfigurationClient implements IConfiguration {
  private static readonly CLIENT_VERSION = "0.1.0";

  private readonly _applicationName: string;
  private readonly _configurationDirectory: string;
  private readonly _store: ConfigurationStore;
  private readonly _logger: IConfigurationLogger;

  private constructor(
    applicationName: string,
    configurationDirectory: string,
    store: ConfigurationStore,
    logger: IConfigurationLogger
  ) {
    this._applicationName = applicationName;
    this._configurationDirectory = configurationDirectory;
    this._store = store;
    this._logger = logger;
  }

  /**
   * Initializes a ConfigurationClient according to the supplied options.
   */
  public static async initialize(options: ConfigurationOptions): Promise<Result<ConfigurationClient>> {
    if (!options.applicationName || options.applicationName.trim().length === 0) {
      return fail("applicationName must be specified.");
    }

    const logger = options.logger || new NullConfigurationLogger();
    const manifestPath = options.configManifest || ConfigurationManifestReader.defaultManifestPath();

    // 1. Resolve or provision manifest section
    let configDir: string;
    let lockTimeoutMs = options.lockTimeoutMs || 10000;

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, options.applicationName);
    if (readRes.ok) {
      configDir = readRes.value.configurationDirectory;
      lockTimeoutMs = options.lockTimeoutMs || readRes.value.lockTimeoutMs;
    } else {
      // First use: provision manifest section
      const defaultDir = options.configDirectory || path.join(process.cwd(), ".nyssa-ai", "config");
      const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
        manifestPath,
        options.applicationName,
        defaultDir,
        lockTimeoutMs
      );
      if (!writeRes.ok) {
        return fail(`Failed to provision manifest section: ${writeRes.error}`);
      }
      configDir = defaultDir;
    }

    // 2. Ensure protected configuration directory exists
    const dirRes = ProtectedFileSystem.createDirectory(configDir);
    if (!dirRes.ok) {
      return fail(dirRes.error);
    }

    // 3. Resolve master key via Keyring
    const keyring = options.keyring || new KeyringProtectedFile(configDir);
    const keyRes = await KeyringDefault.getOrCreateKey(options.applicationName, keyring);
    if (!keyRes.ok) {
      return fail(keyRes.error);
    }

    const keyBuf = keyRes.value;
    const cipherRes = ConfigurationCipherAesGcm.create(keyBuf);

    // Memory hygiene: wipe temporary key bytes
    keyBuf.fill(0);

    if (!cipherRes.ok) {
      return fail(cipherRes.error);
    }

    const store = new ConfigurationStore(configDir, lockTimeoutMs, cipherRes.value);
    const client = new ConfigurationClient(options.applicationName, configDir, store, logger);

    return ok(client);
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
      return ok(res.value as T);
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
        options?.encrypted || false,
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
        options?.encrypted || false,
        options?.provider,
        signal
      );
    });
  }

  public async remove(namespace: string, key: string, signal?: AbortSignal): Promise<Result<boolean>> {
    return this.executeLogged("remove", async () => {
      return this._store.remove(namespace, key, signal);
    });
  }

  private async executeLogged<T>(operation: string, action: () => Promise<Result<T>>): Promise<Result<T>> {
    const start = performance.now();
    const correlationId = crypto.randomUUID();

    try {
      const result = await action();
      const durationMs = Math.round(performance.now() - start);

      const level: LogLevel = result.ok ? "info" : "warn";
      this.safeLog({
        timestamp: new Date().toISOString(),
        level,
        event: `configuration.${operation}.${result.ok ? "completed" : "failed"}`,
        service: this._applicationName,
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
        service: this._applicationName,
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
