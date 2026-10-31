// Responsibility: High-level store coordinating namespace document operations, cipher sealing, external vaults, OAuth grants, and crash recovery.
// Public interface:
// `-- ConfigurationStore
//     +-- authorize(namespace, key, options, signal)
//     +-- authorizeProvider(providerName, signal)
//     +-- get(namespace, key, signal)
//     +-- getNamespace(namespace, signal)
//     +-- list(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- move(namespace, key, destinationProvider, signal)
//     +-- new(configurationDirectory, lockTimeoutMs, cipher, options)
//     +-- reauthorize(namespace, key, signal)
//     +-- recover(handle, signal)
//     +-- remove(namespace, key, signal)
//     +-- resolveVaultCredential(vaultName, signal)
//     +-- set(namespace, key, value, encrypted, provider, signal)
//     +-- setDocumentWriteInterceptor(interceptor)
//     `-- setNamespace(namespace, value, signal)

import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationDocumentLocalFile } from "./ConfigurationDocumentLocalFile.js";
import { ConfigurationEntryResolver } from "./ConfigurationEntryResolver.js";
import { NamespaceDocumentHandler } from "./NamespaceDocumentHandler.js";
import { ConfigurationMover } from "./ConfigurationMover.js";
import { OAuthTokenManager } from "../oauth/OAuthTokenManager.js";
import { PendingJournalManager } from "../recovery/PendingJournalManager.js";
import type { ConfigurationWriteRecoveryHandle } from "../recovery/ConfigurationWriteRecoveryHandle.js";
import type { ConfigurationRemoval } from "./ConfigurationRemoval.js";
import type { ConfigurationMove } from "./ConfigurationMove.js";
import type { IConfigurationCipher } from "../encryption/IConfigurationCipher.js";
import type { IVaultProvider } from "../vault/IVaultProvider.js";
import type { OAuthOptions } from "../oauth/OAuthOptions.js";
import type { IOAuthFlowClient } from "../oauth/IOAuthFlowClient.js";
import type { IBrowserLauncher } from "../oauth/IBrowserLauncher.js";

/**
 * ConfigurationStore maps namespace names to document files and transparently seals/unseals values,
 * routes remote vault entries, refreshes OAuth tokens, and replays write-ahead journals.
 */
export class ConfigurationStore {
  private readonly _vaultCredentials: ReadonlyMap<string, { readonly namespace: string; readonly key: string }>;
  private readonly _vaultAuth: ReadonlyMap<string, { readonly namespace: string; readonly key: string; readonly options?: OAuthOptions | undefined }>;
  private readonly _resolver: ConfigurationEntryResolver;
  private readonly _namespaceHandler: NamespaceDocumentHandler;
  private readonly _mover: ConfigurationMover;

  public constructor(
    configurationDirectory: string,
    lockTimeoutMs: number = 10000,
    cipher?: IConfigurationCipher | undefined,
    options?: {
      readonly vaultProviders?: ReadonlyMap<string, IVaultProvider> | undefined;
      readonly vaultCredentials?: ReadonlyMap<string, { readonly namespace: string; readonly key: string }> | undefined;
      readonly vaultAuth?: ReadonlyMap<string, { readonly namespace: string; readonly key: string; readonly options?: OAuthOptions | undefined }> | undefined;
      readonly oauthClient?: IOAuthFlowClient | undefined;
      readonly browserLauncher?: IBrowserLauncher | undefined;
      readonly clock?: (() => number) | undefined;
    }
  ) {
    const resolvedDir = path.resolve(configurationDirectory);
    const vaultProviders = options?.vaultProviders ?? new Map<string, IVaultProvider>();
    this._vaultCredentials = options?.vaultCredentials ?? new Map();
    this._vaultAuth = options?.vaultAuth ?? new Map();
    const oauthManager = new OAuthTokenManager(resolvedDir, cipher, options?.oauthClient, options?.browserLauncher, options?.clock);
    const journalManager = new PendingJournalManager(resolvedDir);
    this._resolver = new ConfigurationEntryResolver(
      resolvedDir, lockTimeoutMs, cipher, vaultProviders, this._vaultCredentials, oauthManager, journalManager
    );
    this._namespaceHandler = new NamespaceDocumentHandler(this._resolver);
    this._mover = new ConfigurationMover(this._resolver);
  }

  public setDocumentWriteInterceptor(interceptor?: (namespace: string) => Result<void>): void {
    this._resolver.setDocumentWriteInterceptor(interceptor);
  }

  public async resolveVaultCredential(vaultName: string, signal?: AbortSignal): Promise<Result<string>> {
    signal?.throwIfAborted();
    const cred = this._vaultCredentials.get(vaultName);
    if (!cred) {
      return fail(`configuration: provider '${vaultName}' declares no credential in the manifest`, "Validation");
    }

    const res = await this.get(cred.namespace, cred.key, signal);
    if (!res.ok) {
      if (res.category === "NotFound") {
        return fail(
          `configuration: authorization required for provider '${vaultName}'; no credential has been acquired for it yet`,
          "Forbidden"
        );
      }
      return res;
    }

    return ok(typeof res.value === "string" ? res.value : JSON.stringify(res.value));
  }

  public async get(namespace: string, key: string, signal?: AbortSignal): Promise<Result<unknown>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) return validRes;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) return readRes;

    const entry = readRes.value[key];
    if (!entry) {
      return fail(`No configuration entry exists at '${namespace}/${key}'.`, "NotFound");
    }

    return this._resolver.resolveEntryValue(namespace, key, entry, signal);
  }

  public async set(
    namespace: string,
    key: string,
    value: unknown,
    encrypted?: boolean,
    provider?: string,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) return validRes;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    return doc.modify(async (current) => {
      const existing = current[key];
      const existingProvider = this._resolver.normalizeProviderName(existing);

      let targetProvider: string;
      if (provider !== undefined) {
        targetProvider = this._resolver.canonicalProvider(provider);
      } else if (encrypted === true) {
        targetProvider = existingProvider !== "Default" ? existingProvider : "aesLocal";
      } else if (encrypted === false) {
        targetProvider = "Default";
      } else {
        targetProvider = existingProvider;
      }

      if (existing !== undefined && targetProvider !== existingProvider) {
        return fail(
          `configuration: '${namespace}/${key}' is held by '${existingProvider}'; use move to change where it lives, which withdraws the copy it leaves behind`,
          "Conflict"
        );
      }

      const circularRes = this._resolver.checkCircularCredential(targetProvider, namespace, key);
      if (!circularRes.ok) return circularRes;

      const buildRes = await this._resolver.prepareEntryForProvider(
        namespace,
        key,
        value,
        targetProvider,
        signal
      );
      if (!buildRes.ok) return buildRes;

      if (buildRes.value.vaultWrite) {
        const vw = buildRes.value.vaultWrite;
        const vault = this._resolver.vaultProviders.get(vw.provider);
        if (!vault) {
          return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
        }
        const setSecretRes = await vault.setSecret(vw.secretName, vw.value, signal);
        if (!setSecretRes.ok) return setSecretRes;
      }

      const interceptRes = this._resolver.invokeWriteInterceptor(namespace);
      if (!interceptRes.ok) return interceptRes;

      current[key] = buildRes.value.entry;
      return ok(current);
    }, signal);
  }

  public async list(namespace: string, signal?: AbortSignal): Promise<Result<readonly string[]>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!validNs.ok) return validNs;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) return readRes;

    const sortedKeys = Object.keys(readRes.value).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
    return ok(sortedKeys);
  }

  public async remove(
    namespace: string,
    key: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationRemoval>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) return validRes;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    let remoteValueRemains = false;

    const modRes = await doc.modify(async (current) => {
      if (!Object.prototype.hasOwnProperty.call(current, key)) {
        this._resolver.oauthManager.deletePendingGrant(namespace, key);
        return ok(current);
      }

      const existing = current[key]!;
      const providerName = this._resolver.normalizeProviderName(existing);

      const circularRes = this._resolver.checkCircularCredential(providerName, namespace, key);
      if (!circularRes.ok) return circularRes;

      if (providerName !== "Default" && providerName !== "aesLocal" && providerName !== "oauth") {
        const vault = this._resolver.vaultProviders.get(providerName);
        if (!vault) {
          return fail(`Configuration provider '${providerName}' is not registered.`, "Validation");
        }
        const secretName = typeof existing.value === "string" ? existing.value : `${namespace}/${key}`;
        const delRes = await vault.deleteSecret(secretName, signal);
        if (!delRes.ok) return delRes;
        remoteValueRemains = delRes.value.retained;
      }

      this._resolver.oauthManager.deletePendingGrant(namespace, key);
      delete current[key];
      return ok(current);
    }, signal);

    if (!modRes.ok) return modRes;
    return ok({ remoteValueRemains });
  }

  public async move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>> {
    return this._mover.move(namespace, key, destinationProvider, signal);
  }

  public async getNamespace<T>(namespace: string, signal?: AbortSignal): Promise<Result<T>> {
    return this._namespaceHandler.getNamespace<T>(namespace, signal);
  }

  public async setNamespace<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>> {
    return this._namespaceHandler.setNamespace<T>(namespace, value, signal);
  }

  public async mergeSettings<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>> {
    return this._namespaceHandler.mergeSettings<T>(namespace, value, signal);
  }

  public async authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) return validRes;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    return doc.modify(async (current) => {
      const existing = current[key];
      const existingProvider = this._resolver.normalizeProviderName(existing);
      if (existing !== undefined && existingProvider !== "aesLocal" && existingProvider !== "oauth") {
        return fail(
          `configuration: '${namespace}/${key}' is held by '${existingProvider}'; use move to change where it lives`,
          "Conflict"
        );
      }

      const authRes = await this._resolver.oauthManager.authorize(namespace, key, options, signal);
      if (!authRes.ok) return authRes;

      current[key] = authRes.value;
      return ok(current);
    }, signal);
  }

  public async authorizeProvider(providerName: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const info = this._vaultAuth.get(providerName);
    if (!info) {
      return fail(`Provider '${providerName}' is not configured with an OAuth credential.`, "Validation");
    }
    if (!info.options) {
      return fail(`Provider '${providerName}' does not supply OAuth authorization options.`, "Validation");
    }
    return this.authorize(info.namespace, info.key, info.options, signal);
  }

  public async reauthorize(namespace: string, key: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) return validRes;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) return readRes;

    const existing = readRes.value[key];
    if (!existing) {
      return fail(`No OAuth entry found at '${namespace}/${key}'.`, "NotFound");
    }

    const optsRes = this._resolver.oauthManager.extractGrantOptions(namespace, key, existing);
    if (!optsRes.ok) return optsRes;

    return this.authorize(namespace, key, optsRes.value, signal);
  }

  public async recover(
    handle: ConfigurationWriteRecoveryHandle,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(handle.namespace, "namespace");
    if (!validNs.ok) return validNs;

    return this._resolver.replayPendingJournalsIfPresent(handle.namespace, signal, handle.operationId);
  }
}
