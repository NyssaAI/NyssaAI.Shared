// Responsibility: Primary entrypoint orchestrating manifest resolution, keyring custody, vault factories, and configuration store operations.
// Public interface:
// `-- ConfigurationClient
//     +-- applicationName
//     +-- authorize(namespace, key, options, signal)
//     +-- authorizeProvider(providerName, signal)
//     +-- configurationDirectory
//     +-- get(namespace, key, signal)
//     +-- getAs(namespace, key, signal)
//     +-- getNamespace(namespace, signal)
//     +-- initialize(options)
//     +-- list(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- move(namespace, key, destinationProvider, signal)
//     +-- reauthorize(namespace, key, signal)
//     +-- recover(handle, signal)
//     +-- remove(namespace, key, signal)
//     +-- set(namespace, key, value, options, signal)
//     +-- setAs(namespace, key, value, options, signal)
//     +-- setDocumentWriteInterceptor(interceptor)
//     `-- setNamespace(namespace, value, signal)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "./results/Result.js";
import { ConfigurationBootstrap } from "./ConfigurationBootstrap.js";
import type { ConfigurationStore } from "./values/ConfigurationStore.js";
import type { Configuration } from "./Configuration.js";
import type { ConfigurationReader } from "./ConfigurationReader.js";
import type { ConfigurationWriter } from "./ConfigurationWriter.js";
import type { ConfigurationOAuthWriter } from "./ConfigurationOAuthWriter.js";
import type { ConfigurationMaintainer } from "./ConfigurationMaintainer.js";
import type { IConfiguration } from "./IConfiguration.js";
import type { SetOptions } from "./SetOptions.js";
import type { ConfigurationOptions } from "./ConfigurationOptions.js";
import type { ConfigurationRemoval } from "./values/ConfigurationRemoval.js";
import type { ConfigurationMove } from "./values/ConfigurationMove.js";
import type { OAuthOptions } from "./oauth/OAuthOptions.js";
import type { ConfigurationWriteRecoveryHandle } from "./recovery/ConfigurationWriteRecoveryHandle.js";
import type { IConfigurationLogger } from "./logging/IConfigurationLogger.js";
import type { LogRecord } from "./logging/LogRecord.js";

/**
 * ConfigurationClient orchestrates manifest resolution, master key custody, external vaults,
 * OAuth 2.0 PKCE credentials, and cross-process namespace documents.
 */
export class ConfigurationClient {
  private static readonly CLIENT_VERSION = "0.3.1";

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
    const bootRes = await ConfigurationBootstrap.createStore(options);
    if (!bootRes.ok) {
      return bootRes;
    }

    const { applicationName, configurationDirectory, store, logger } = bootRes.value;
    return ok(new ConfigurationClient(applicationName, configurationDirectory, store, logger));
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

  public async authorizeProvider(providerName: string, signal?: AbortSignal): Promise<Result<void>> {
    return this.executeLogged("authorizeProvider", async () => {
      return this._store.authorizeProvider(providerName, signal);
    });
  }

  public async reauthorize(namespace: string, key: string, signal?: AbortSignal): Promise<Result<void>> {
    return this.executeLogged("reauthorize", async () => {
      return this._store.reauthorize(namespace, key, signal);
    });
  }

  public async recover(
    handle: ConfigurationWriteRecoveryHandle,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    return this.executeLogged("recover", async () => {
      return this._store.recover(handle, signal);
    });
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

const _typeCheck: Configuration = null as unknown as ConfigurationClient;
const _typeCheckReader: ConfigurationReader = _typeCheck;
const _typeCheckWriter: ConfigurationWriter = _typeCheck;
const _typeCheckOAuth: ConfigurationOAuthWriter = _typeCheck;
const _typeCheckMaintainer: ConfigurationMaintainer = _typeCheck;
const _typeCheckLegacy: IConfiguration = _typeCheck;
void _typeCheck;
void _typeCheckReader;
void _typeCheckWriter;
void _typeCheckOAuth;
void _typeCheckMaintainer;
void _typeCheckLegacy;
