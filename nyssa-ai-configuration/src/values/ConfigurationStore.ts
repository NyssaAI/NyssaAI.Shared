// Responsibility: High-level store coordinating namespace document operations, cipher sealing, external vaults, OAuth grants, and crash recovery.
// Public interface:
// `-- ConfigurationStore
//     +-- authorize(namespace, key, options, signal)
//     +-- get(namespace, key, signal)
//     +-- getNamespace(namespace, signal)
//     +-- list(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- move(namespace, key, destinationProvider, signal)
//     +-- new(configurationDirectory, lockTimeoutMs, cipher, options)
//     +-- recoverWrite(handle, signal)
//     +-- remove(namespace, key, signal)
//     +-- resolveVaultCredential(vaultName, signal)
//     +-- set(namespace, key, value, encrypted, provider, signal)
//     +-- setDocumentWriteInterceptor(interceptor)
//     `-- setNamespace(namespace, value, signal)

import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationDocumentLocalFile } from "./ConfigurationDocumentLocalFile.js";
import { OAuthTokenManager } from "../oauth/OAuthTokenManager.js";
import { PendingJournalManager } from "../recovery/PendingJournalManager.js";
import type { ConfigurationWriteRecoveryHandle } from "../recovery/ConfigurationWriteRecoveryHandle.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";
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
  private static readonly LEGACY_SETTINGS_KEY = "namespaceSettings";

  private readonly _configurationDirectory: string;
  private readonly _lockTimeoutMs: number;
  private readonly _cipher?: IConfigurationCipher | undefined;
  private readonly _vaultProviders: ReadonlyMap<string, IVaultProvider>;
  private readonly _vaultCredentials: ReadonlyMap<string, { readonly namespace: string; readonly key: string }>;
  private readonly _oauthManager: OAuthTokenManager;
  private readonly _journalManager: PendingJournalManager;
  private _documentWriteInterceptor?: ((namespace: string) => Result<void>) | undefined;

  public constructor(
    configurationDirectory: string,
    lockTimeoutMs: number = 10000,
    cipher?: IConfigurationCipher | undefined,
    options?: {
      readonly vaultProviders?: ReadonlyMap<string, IVaultProvider> | undefined;
      readonly vaultCredentials?: ReadonlyMap<string, { readonly namespace: string; readonly key: string }> | undefined;
      readonly oauthClient?: IOAuthFlowClient | undefined;
      readonly browserLauncher?: IBrowserLauncher | undefined;
      readonly clock?: (() => number) | undefined;
    }
  ) {
    this._configurationDirectory = path.resolve(configurationDirectory);
    this._lockTimeoutMs = lockTimeoutMs;
    this._cipher = cipher;
    this._vaultProviders = options?.vaultProviders ?? new Map<string, IVaultProvider>();
    this._vaultCredentials =
      options?.vaultCredentials ?? new Map<string, { readonly namespace: string; readonly key: string }>();
    this._oauthManager = new OAuthTokenManager(
      this._configurationDirectory,
      cipher,
      options?.oauthClient,
      options?.browserLauncher,
      options?.clock
    );
    this._journalManager = new PendingJournalManager(this._configurationDirectory);
  }

  /**
   * Registers an optional write interceptor used in recovery tests to simulate disk write failures.
   */
  public setDocumentWriteInterceptor(interceptor?: (namespace: string) => Result<void>): void {
    this._documentWriteInterceptor = interceptor;
  }

  /**
   * Resolves the bearer credential configured for a vault provider.
   */
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
    const validRes = this.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const doc = this.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) {
      return readRes;
    }

    const entry = readRes.value[key];
    if (!entry) {
      return fail(`No configuration entry exists at '${namespace}/${key}'.`, "NotFound");
    }

    return this.resolveEntryValue(namespace, key, entry, signal);
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
    const validRes = this.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const doc = this.getDocument(namespace);
    return doc.modify(async (current) => {
      const existing = current[key];
      const existingProvider = this.normalizeProviderName(existing);

      let targetProvider: string;
      if (provider !== undefined) {
        targetProvider = this.canonicalProvider(provider);
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

      const circularRes = this.checkCircularCredential(targetProvider, namespace, key);
      if (!circularRes.ok) {
        return circularRes;
      }

      const buildRes = await this.prepareEntryForProvider(namespace, key, value, targetProvider, signal);
      if (!buildRes.ok) {
        return buildRes;
      }

      if (buildRes.value.vaultWrite) {
        const vw = buildRes.value.vaultWrite;
        const vault = this._vaultProviders.get(vw.provider);
        if (!vault) {
          return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
        }
        const setSecretRes = await vault.setSecret(vw.secretName, vw.value, signal);
        if (!setSecretRes.ok) {
          return setSecretRes;
        }
      }

      if (this._documentWriteInterceptor) {
        const interceptRes = this._documentWriteInterceptor(namespace);
        if (!interceptRes.ok) {
          return interceptRes;
        }
      }

      current[key] = buildRes.value.entry;
      return ok(current);
    }, signal);
  }

  public async list(namespace: string, signal?: AbortSignal): Promise<Result<readonly string[]>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!validNs.ok) {
      return validNs;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const doc = this.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) {
      return readRes;
    }

    const sortedKeys = Object.keys(readRes.value).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
    return ok(sortedKeys);
  }

  public async remove(
    namespace: string,
    key: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationRemoval>> {
    signal?.throwIfAborted();
    const validRes = this.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const doc = this.getDocument(namespace);
    let remoteValueRemains = false;

    const modRes = await doc.modify(async (current) => {
      if (!Object.prototype.hasOwnProperty.call(current, key)) {
        this._oauthManager.deletePendingGrant(namespace, key);
        return ok(current);
      }

      const existing = current[key]!;
      const providerName = this.normalizeProviderName(existing);

      const circularRes = this.checkCircularCredential(providerName, namespace, key);
      if (!circularRes.ok) {
        return circularRes;
      }

      if (providerName !== "Default" && providerName !== "aesLocal") {
        const vault = this._vaultProviders.get(providerName);
        if (!vault) {
          return fail(`Configuration provider '${providerName}' is not registered.`, "Validation");
        }
        const secretName = typeof existing.value === "string" ? existing.value : `${namespace}/${key}`;
        const delRes = await vault.deleteSecret(secretName, signal);
        if (!delRes.ok) {
          return delRes;
        }
        remoteValueRemains = delRes.value.retained;
      }

      this._oauthManager.deletePendingGrant(namespace, key);
      delete current[key];
      return ok(current);
    }, signal);

    if (!modRes.ok) {
      return modRes;
    }

    return ok({ remoteValueRemains });
  }

  public async move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>> {
    signal?.throwIfAborted();
    const validRes = this.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const targetProvider = this.canonicalProvider(destinationProvider);
    const circularRes = this.checkCircularCredential(targetProvider, namespace, key);
    if (!circularRes.ok) {
      return circularRes;
    }

    if (targetProvider !== "Default" && targetProvider !== "aesLocal" && !this._vaultProviders.has(targetProvider)) {
      return fail(`Configuration provider '${targetProvider}' is not registered.`, "Validation");
    }

    const doc = this.getDocument(namespace);
    let sourceProvider = "Default";
    let sourceSecretName = `${namespace}/${key}`;
    let wasNoOp = false;

    const modRes = await doc.modify(async (current) => {
      const existing = current[key];
      if (!existing) {
        return fail(`No configuration entry exists at '${namespace}/${key}'.`, "NotFound");
      }

      sourceProvider = this.normalizeProviderName(existing);
      if (sourceProvider === targetProvider) {
        wasNoOp = true;
        return ok(current);
      }

      if (sourceProvider !== "Default" && sourceProvider !== "aesLocal" && typeof existing.value === "string") {
        sourceSecretName = existing.value;
      }

      // 1. Read cleartext value from current provider
      const clearRes = await this.resolveEntryValue(namespace, key, existing, signal);
      if (!clearRes.ok) {
        return clearRes;
      }

      // 2. Prepare and write to destination provider first
      const preparedRes = await this.prepareEntryForProvider(
        namespace,
        key,
        clearRes.value,
        targetProvider,
        signal
      );
      if (!preparedRes.ok) {
        return preparedRes;
      }

      if (preparedRes.value.vaultWrite) {
        const vw = preparedRes.value.vaultWrite;
        const destVault = this._vaultProviders.get(vw.provider);
        if (!destVault) {
          return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
        }
        const writeDestRes = await destVault.setSecret(vw.secretName, vw.value, signal);
        if (!writeDestRes.ok) {
          return writeDestRes;
        }
      }

      // 3. If moving away from a remote vault, record a .pending/<ns>.move.json before updating local document
      if (sourceProvider !== "Default" && sourceProvider !== "aesLocal") {
        const moveJournalRes = this._journalManager.createMoveJournal(
          namespace,
          key,
          sourceProvider,
          sourceSecretName
        );
        if (!moveJournalRes.ok) {
          return moveJournalRes;
        }
      }

      if (this._documentWriteInterceptor) {
        const interceptRes = this._documentWriteInterceptor(namespace);
        if (!interceptRes.ok) {
          return interceptRes;
        }
      }

      current[key] = preparedRes.value.entry;
      return ok(current);
    }, signal);

    if (!modRes.ok) {
      return modRes;
    }

    if (wasNoOp) {
      return ok({ sourceValueRemains: false });
    }

    // 4. If source was a remote vault, delete the old copy and clean up the move journal
    if (sourceProvider !== "Default" && sourceProvider !== "aesLocal") {
      const sourceVault = this._vaultProviders.get(sourceProvider);
      if (!sourceVault) {
        return ok({
          sourceValueRemains: true,
          sourceDeleteError: `Source provider '${sourceProvider}' is not registered.`
        });
      }

      const delRes = await sourceVault.deleteSecret(sourceSecretName, signal);
      if (!delRes.ok) {
        // Leave .pending/<ns>.move.json or return sourceDeleteError?
        // Notice: F-201 UC-003 expects sourceValueRemains: true and sourceDeleteError when source delete fails,
        // and F-204 UC-003 tests when the process crashes BEFORE source delete runs (or if source delete is retried).
        this._journalManager.deleteMoveJournal(namespace);
        return ok({
          sourceValueRemains: true,
          sourceDeleteError: delRes.error
        });
      }

      this._journalManager.deleteMoveJournal(namespace);
      return ok({
        sourceValueRemains: delRes.value.retained
      });
    }

    return ok({ sourceValueRemains: false });
  }

  public async getNamespace<T>(namespace: string, signal?: AbortSignal): Promise<Result<T>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!validNs.ok) {
      return validNs;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const doc = this.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) {
      return readRes;
    }

    const keys = Object.keys(readRes.value);
    if (keys.length === 0) {
      return fail(
        `No configuration entry exists at '${namespace}/${ConfigurationStore.LEGACY_SETTINGS_KEY}'.`,
        "NotFound"
      );
    }

    if (Object.prototype.hasOwnProperty.call(readRes.value, ConfigurationStore.LEGACY_SETTINGS_KEY)) {
      const legacyEntry = readRes.value[ConfigurationStore.LEGACY_SETTINGS_KEY]!;
      const resolvedLegacy = await this.resolveEntryValue(
        namespace,
        ConfigurationStore.LEGACY_SETTINGS_KEY,
        legacyEntry,
        signal
      );
      if (!resolvedLegacy.ok) {
        return resolvedLegacy;
      }

      if (typeof resolvedLegacy.value === "string") {
        try {
          const parsed = JSON.parse(resolvedLegacy.value) as T;
          if (parsed === null) {
            return fail(
              `The configuration value at '${namespace}/${ConfigurationStore.LEGACY_SETTINGS_KEY}' deserialized to null.`,
              "Validation"
            );
          }
          return ok(parsed);
        } catch {
          return ok(resolvedLegacy.value as unknown as T);
        }
      }
      if (resolvedLegacy.value === null) {
        return fail(
          `The configuration value at '${namespace}/${ConfigurationStore.LEGACY_SETTINGS_KEY}' deserialized to null.`,
          "Validation"
        );
      }
      return ok(resolvedLegacy.value as T);
    }

    const assembled: Record<string, unknown> = {};
    for (const [k, entry] of Object.entries(readRes.value)) {
      signal?.throwIfAborted();
      const valRes = await this.resolveEntryValue(namespace, k, entry, signal);
      if (!valRes.ok) {
        return valRes;
      }
      assembled[k] = valRes.value;
    }

    return ok(assembled as T);
  }

  public async setNamespace<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>> {
    return this.writeNamespaceInternal(namespace, value, true, signal);
  }

  public async mergeSettings<T>(namespace: string, value: T, signal?: AbortSignal): Promise<Result<void>> {
    return this.writeNamespaceInternal(namespace, value, false, signal);
  }

  public async authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validRes = this.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const doc = this.getDocument(namespace);
    return doc.modify(async (current) => {
      const existing = current[key];
      const existingProvider = this.normalizeProviderName(existing);
      if (existing !== undefined && existingProvider !== "aesLocal") {
        return fail(
          `configuration: '${namespace}/${key}' is held by '${existingProvider}'; use move to change where it lives`,
          "Conflict"
        );
      }

      const authRes = await this._oauthManager.authorize(namespace, key, options, signal);
      if (!authRes.ok) {
        return authRes;
      }

      current[key] = authRes.value;
      return ok(current);
    }, signal);
  }

  public async recoverWrite(
    handle: ConfigurationWriteRecoveryHandle,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(handle.namespace, "namespace");
    if (!validNs.ok) {
      return validNs;
    }

    return this.replayPendingJournalsIfPresent(handle.namespace, signal, handle.operationId);
  }

  private async writeNamespaceInternal<T>(
    namespace: string,
    value: T,
    replace: boolean,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!validNs.ok) {
      return validNs;
    }

    if (value === undefined) {
      return fail("Namespace value cannot be undefined.", "Validation");
    }

    const isPlainObject = typeof value === "object" && value !== null && !Array.isArray(value);
    if (!replace && !isPlainObject) {
      return fail(
        "Merging namespace settings requires JSON objects for the supplied value and any legacy namespaceSettings value.",
        "Validation"
      );
    }

    const replayRes = await this.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    if (isPlainObject) {
      const obj = value as Record<string, unknown>;
      if (Object.prototype.hasOwnProperty.call(obj, ConfigurationStore.LEGACY_SETTINGS_KEY)) {
        return fail(
          "Namespace settings require valid JSON properties without duplicate names or the reserved namespaceSettings name.",
          "Validation"
        );
      }
      for (const propKey of Object.keys(obj)) {
        const keyValid = ConfigurationDocumentLocalFile.validateSegment(propKey, "key");
        if (!keyValid.ok) {
          return keyValid;
        }
      }
    }

    const doc = this.getDocument(namespace);
    const docPath = this.getDocumentPath(namespace);

    return doc.modify(async (current) => {
      const expectedExistingSha256 = PendingJournalManager.computeDocumentSha256(docPath);

      if (!isPlainObject) {
        // Scalar/array shorthand in replace mode
        const existingLegacy = current[ConfigurationStore.LEGACY_SETTINGS_KEY];
        const legacyProvider = this.normalizeProviderName(existingLegacy);
        if (existingLegacy !== undefined && legacyProvider !== "Default") {
          return fail(
            `configuration: '${namespace}/${ConfigurationStore.LEGACY_SETTINGS_KEY}' is held by '${legacyProvider}'; use move to change where it lives`,
            "Conflict"
          );
        }
        const replaced: Record<string, ConfigurationValue> = {
          [ConfigurationStore.LEGACY_SETTINGS_KEY]: {
            value
          }
        };
        return ok(replaced);
      }

      const working: Record<string, ConfigurationValue> = { ...current };

      // Check legacy namespaceSettings migration
      if (Object.prototype.hasOwnProperty.call(working, ConfigurationStore.LEGACY_SETTINGS_KEY)) {
        const legacyEntry = working[ConfigurationStore.LEGACY_SETTINGS_KEY]!;
        const legacyProvider = this.normalizeProviderName(legacyEntry);
        if (legacyProvider !== "Default") {
          return fail(
            `configuration: '${namespace}/${ConfigurationStore.LEGACY_SETTINGS_KEY}' is held by '${legacyProvider}'; use move to change where it lives`,
            "Conflict"
          );
        }

        if (!replace) {
          let parsedLegacy: unknown = legacyEntry.value;
          if (typeof legacyEntry.value === "string") {
            try {
              parsedLegacy = JSON.parse(legacyEntry.value);
            } catch {
              return fail(
                "Merging namespace settings requires JSON objects for the supplied value and any legacy namespaceSettings value.",
                "Validation"
              );
            }
          }
          if (typeof parsedLegacy !== "object" || parsedLegacy === null || Array.isArray(parsedLegacy)) {
            return fail(
              "Merging namespace settings requires JSON objects for the supplied value and any legacy namespaceSettings value.",
              "Validation"
            );
          }
          for (const [lk, lv] of Object.entries(parsedLegacy as Record<string, unknown>)) {
            if (!Object.prototype.hasOwnProperty.call(working, lk)) {
              working[lk] = { value: lv };
            }
          }
        }
        delete working[ConfigurationStore.LEGACY_SETTINGS_KEY];
      }

      const incomingObj = value as Record<string, unknown>;
      const plannedDocument: Record<string, ConfigurationValue> = replace ? {} : { ...working };
      const vaultWrites: Array<{
        readonly provider: string;
        readonly secretName: string;
        readonly value: string;
      }> = [];

      // Validate all target providers before making any writes
      for (const [propKey, propVal] of Object.entries(incomingObj)) {
        const existingEntry = working[propKey];
        const providerName = this.normalizeProviderName(existingEntry);

        const circularRes = this.checkCircularCredential(providerName, namespace, propKey);
        if (!circularRes.ok) {
          return circularRes;
        }

        const prepared = await this.prepareEntryForProvider(
          namespace,
          propKey,
          propVal,
          providerName,
          signal
        );
        if (!prepared.ok) {
          return prepared;
        }

        plannedDocument[propKey] = prepared.value.entry;
        if (prepared.value.vaultWrite) {
          vaultWrites.push(prepared.value.vaultWrite);
        }
      }

      // Write-ahead journal before performing vault writes or local publication
      const journalRes = this._journalManager.createWriteJournal(
        namespace,
        expectedExistingSha256,
        plannedDocument,
        vaultWrites
      );
      if (!journalRes.ok) {
        return journalRes;
      }
      const recoveryHandle = journalRes.value;

      // Stage 1: Execute vault writes
      for (const vw of vaultWrites) {
        signal?.throwIfAborted();
        const vault = this._vaultProviders.get(vw.provider);
        if (!vault) {
          return fail(
            `Configuration provider '${vw.provider}' is not registered.`,
            "Validation",
            recoveryHandle
          );
        }
        const setRes = await vault.setSecret(vw.secretName, vw.value, signal);
        if (!setRes.ok) {
          return fail(setRes.error, setRes.category ?? "Unavailable", recoveryHandle);
        }
      }

      // Transition journal to stage: 'local'
      const stageRes = this._journalManager.updateWriteJournalStage(namespace, "local");
      if (!stageRes.ok) {
        return fail(stageRes.error, stageRes.category ?? "Unavailable", recoveryHandle);
      }

      if (this._documentWriteInterceptor) {
        const interceptRes = this._documentWriteInterceptor(namespace);
        if (!interceptRes.ok) {
          return fail(interceptRes.error, interceptRes.category ?? "Unavailable", recoveryHandle);
        }
      }

      this._journalManager.deleteWriteJournal(namespace);
      return ok(plannedDocument);
    }, signal);
  }

  private async replayPendingJournalsIfPresent(
    namespace: string,
    signal?: AbortSignal,
    expectedOperationId?: string
  ): Promise<Result<void>> {
    signal?.throwIfAborted();

    // 1. Check pending move cleanup journal
    if (this._journalManager.hasMoveJournal(namespace)) {
      const moveRead = this._journalManager.readMoveJournal(namespace);
      if (!moveRead.ok) {
        return moveRead;
      }
      const moveRecord = moveRead.value;
      if (moveRecord) {
        const vault = this._vaultProviders.get(moveRecord.sourceProvider);
        if (vault) {
          const delRes = await vault.deleteSecret(moveRecord.sourceSecretName, signal);
          if (!delRes.ok) {
            return delRes;
          }
        }
        this._journalManager.deleteMoveJournal(namespace);
      }
    }

    // 2. Check pending write journal
    if (!this._journalManager.hasWriteJournal(namespace)) {
      return ok(undefined);
    }

    const doc = this.getDocument(namespace);
    const docPath = this.getDocumentPath(namespace);

    return doc.modify(async (current) => {
      const journalRead = this._journalManager.readWriteJournal(namespace);
      if (!journalRead.ok) {
        return journalRead;
      }
      const journal = journalRead.value;
      if (!journal) {
        return ok(current);
      }

      if (expectedOperationId !== undefined && journal.operationId !== expectedOperationId) {
        return fail(
          `Pending journal operationId '${journal.operationId}' does not match recovery handle '${expectedOperationId}'.`,
          "Conflict"
        );
      }

      const currentSha256 = PendingJournalManager.computeDocumentSha256(docPath);

      // Check if manual reconciliation already brought the local document to plannedNewSha256
      if (currentSha256 === journal.plannedNewSha256) {
        for (const vw of journal.vaultWrites) {
          const vault = this._vaultProviders.get(vw.provider);
          if (!vault) {
            return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
          }
          const setRes = await vault.setSecret(vw.secretName, vw.value, signal);
          if (!setRes.ok) {
            return setRes;
          }
        }
        this._journalManager.deleteWriteJournal(namespace);
        return ok(current);
      }

      // Check if another writer modified the file after the interrupted write started
      if (currentSha256 !== journal.expectedExistingSha256) {
        return fail(
          `Configuration in namespace '${namespace}' changed after the interrupted operation started. Recovery stopped to prevent overwriting concurrent changes.`,
          "Conflict"
        );
      }

      // Replay vault writes if still at stage 'vault' (or idempotently ensure they succeeded)
      if (journal.stage === "vault") {
        for (const vw of journal.vaultWrites) {
          signal?.throwIfAborted();
          const vault = this._vaultProviders.get(vw.provider);
          if (!vault) {
            return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
          }
          const setRes = await vault.setSecret(vw.secretName, vw.value, signal);
          if (!setRes.ok) {
            return setRes;
          }
        }
        this._journalManager.updateWriteJournalStage(namespace, "local");
      }

      if (this._documentWriteInterceptor) {
        const interceptRes = this._documentWriteInterceptor(namespace);
        if (!interceptRes.ok) {
          return interceptRes;
        }
      }

      this._journalManager.deleteWriteJournal(namespace);
      return ok(journal.plannedDocument);
    }, signal);
  }

  private async resolveEntryValue(
    namespace: string,
    key: string,
    entry: ConfigurationValue,
    signal?: AbortSignal
  ): Promise<Result<unknown>> {
    signal?.throwIfAborted();
    const providerName = this.normalizeProviderName(entry);

    if (providerName === "Default") {
      if (entry.json && typeof entry.value === "string") {
        try {
          return ok(JSON.parse(entry.value));
        } catch (err) {
          const message = err instanceof Error ? err.message : String(err);
          return fail(`Failed to deserialize JSON value for '${namespace}/${key}': ${message}`, "Validation");
        }
      }
      return ok(entry.value);
    }

    if (providerName === "aesLocal") {
      if (!this._cipher) {
        return fail("Cannot decrypt entry: no cipher configured.", "Unavailable");
      }
      if (typeof entry.value !== "string") {
        return fail("Encrypted entry must carry a string payload.", "Validation");
      }

      const openRes = this._cipher.open(entry.value, namespace, key);
      if (!openRes.ok) {
        return openRes;
      }

      if (OAuthTokenManager.isOAuthGrantPayload(openRes.value)) {
        const doc = this.getDocument(namespace);
        return this._oauthManager.resolveGrantAccessToken(
          namespace,
          key,
          entry,
          async (updatedEntry) => {
            if (this._documentWriteInterceptor) {
              const interceptRes = this._documentWriteInterceptor(namespace);
              if (!interceptRes.ok) {
                return interceptRes;
              }
            }
            return doc.modify((curr) => {
              curr[key] = updatedEntry;
              return ok(curr);
            }, signal);
          },
          signal
        );
      }

      if (entry.json) {
        try {
          return ok(JSON.parse(openRes.value));
        } catch (err) {
          const message = err instanceof Error ? err.message : String(err);
          return fail(`Failed to deserialize JSON value for '${namespace}/${key}': ${message}`, "Validation");
        }
      }

      return openRes;
    }

    // External vault provider
    const vault = this._vaultProviders.get(providerName);
    if (!vault) {
      return fail(`Configuration provider '${providerName}' is not registered.`, "Validation");
    }

    const secretName = typeof entry.value === "string" ? entry.value : `${namespace}/${key}`;
    const secretRes = await vault.getSecret(secretName, signal);
    if (!secretRes.ok) {
      return secretRes;
    }

    if (entry.json) {
      try {
        return ok(JSON.parse(secretRes.value));
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        return fail(`Failed to deserialize JSON value for '${namespace}/${key}': ${message}`, "Validation");
      }
    }

    return secretRes;
  }

  private async prepareEntryForProvider(
    namespace: string,
    key: string,
    value: unknown,
    providerName: string,
    signal?: AbortSignal
  ): Promise<
    Result<{
      readonly entry: ConfigurationValue;
      readonly vaultWrite?:
        | {
            readonly provider: string;
            readonly secretName: string;
            readonly value: string;
          }
        | undefined;
    }>
  > {
    signal?.throwIfAborted();

    if (providerName === "Default") {
      return ok({
        entry: {
          value
        }
      });
    }

    const isJson = typeof value !== "string";
    const serializedString = isJson ? JSON.stringify(value) : (value as string);

    if (providerName === "aesLocal") {
      if (!this._cipher) {
        return fail("Cannot encrypt entry: no cipher configured.", "Unavailable");
      }
      const sealRes = this._cipher.seal(serializedString, namespace, key);
      if (!sealRes.ok) {
        return sealRes;
      }
      return ok({
        entry: {
          value: sealRes.value,
          encrypted: true,
          provider: "aesLocal",
          ...(isJson ? { json: true } : {})
        }
      });
    }

    if (!this._vaultProviders.has(providerName)) {
      return fail(`Configuration provider '${providerName}' is not registered.`, "Validation");
    }

    const pointer = `${namespace}/${key}`;
    return ok({
      entry: {
        value: pointer,
        encrypted: true,
        provider: providerName,
        ...(isJson ? { json: true } : {})
      },
      vaultWrite: {
        provider: providerName,
        secretName: pointer,
        value: serializedString
      }
    });
  }

  private normalizeProviderName(entry?: ConfigurationValue): string {
    if (!entry) {
      return "Default";
    }
    if (entry.provider && entry.provider.trim().length > 0 && entry.provider !== "Default") {
      return entry.provider;
    }
    if (entry.encrypted) {
      return "aesLocal";
    }
    return "Default";
  }

  private canonicalProvider(provider?: string): string {
    if (!provider || provider.trim().length === 0 || provider === "Default") {
      return "Default";
    }
    return provider.trim();
  }

  private checkCircularCredential(providerName: string, namespace: string, key: string): Result<void> {
    if (providerName === "Default" || providerName === "aesLocal") {
      return ok(undefined);
    }
    const cred = this._vaultCredentials.get(providerName);
    if (cred && cred.namespace === namespace && cred.key === key) {
      return fail(
        `configuration: circular credential: '${namespace}/${key}' is the credential provider '${providerName}' authenticates with, so it cannot be stored in '${providerName}' — reading the vault would require reading the vault`,
        "Conflict"
      );
    }
    return ok(undefined);
  }

  private validateNamespaceAndKey(namespace: string, key: string): Result<void> {
    const nsRes = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!nsRes.ok) {
      return nsRes;
    }
    return ConfigurationDocumentLocalFile.validateSegment(key, "key");
  }

  private getDocumentPath(namespace: string): string {
    return path.join(this._configurationDirectory, `${namespace}.json`);
  }

  private getDocument(namespace: string): ConfigurationDocumentLocalFile {
    return new ConfigurationDocumentLocalFile(this.getDocumentPath(namespace), this._lockTimeoutMs);
  }
}
