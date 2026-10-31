// Responsibility: Whole-namespace get, set, and merge operations with legacy namespaceSettings migration and journaled vault writes.
// Public interface:
// `-- NamespaceDocumentHandler
//     +-- getNamespace(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- new(resolver)
//     `-- setNamespace(namespace, value, signal)

import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationDocumentLocalFile } from "./ConfigurationDocumentLocalFile.js";
import { PendingJournalManager } from "../recovery/PendingJournalManager.js";
import type { ConfigurationEntryResolver } from "./ConfigurationEntryResolver.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";

/**
 * NamespaceDocumentHandler implements getNamespace, setNamespace, and mergeSettings
 * across multi-provider namespace documents.
 */
export class NamespaceDocumentHandler {
  private static readonly LEGACY_SETTINGS_KEY = "namespaceSettings";
  private readonly _resolver: ConfigurationEntryResolver;

  public constructor(resolver: ConfigurationEntryResolver) {
    this._resolver = resolver;
  }

  public async getNamespace<T>(namespace: string, signal?: AbortSignal): Promise<Result<T>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!validNs.ok) return validNs;

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    const doc = this._resolver.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) return readRes;

    const keys = Object.keys(readRes.value);
    if (keys.length === 0) {
      return fail(
        `No configuration entry exists at '${namespace}/${NamespaceDocumentHandler.LEGACY_SETTINGS_KEY}'.`,
        "NotFound"
      );
    }

    if (Object.prototype.hasOwnProperty.call(readRes.value, NamespaceDocumentHandler.LEGACY_SETTINGS_KEY)) {
      const legacyEntry = readRes.value[NamespaceDocumentHandler.LEGACY_SETTINGS_KEY]!;
      const resolvedLegacy = await this._resolver.resolveEntryValue(
        namespace,
        NamespaceDocumentHandler.LEGACY_SETTINGS_KEY,
        legacyEntry,
        signal
      );
      if (!resolvedLegacy.ok) return resolvedLegacy;

      if (typeof resolvedLegacy.value === "string") {
        try {
          const parsed = JSON.parse(resolvedLegacy.value) as T;
          if (parsed === null) {
            return fail(
              `The configuration value at '${namespace}/${NamespaceDocumentHandler.LEGACY_SETTINGS_KEY}' deserialized to null.`,
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
          `The configuration value at '${namespace}/${NamespaceDocumentHandler.LEGACY_SETTINGS_KEY}' deserialized to null.`,
          "Validation"
        );
      }
      return ok(resolvedLegacy.value as T);
    }

    const assembled: Record<string, unknown> = {};
    for (const [k, entry] of Object.entries(readRes.value)) {
      signal?.throwIfAborted();
      const valRes = await this._resolver.resolveEntryValue(namespace, k, entry, signal);
      if (!valRes.ok) return valRes;
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

  private async writeNamespaceInternal<T>(
    namespace: string,
    value: T,
    replace: boolean,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const validNs = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    if (!validNs.ok) return validNs;

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

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) return replayRes;

    if (isPlainObject) {
      const obj = value as Record<string, unknown>;
      if (Object.prototype.hasOwnProperty.call(obj, NamespaceDocumentHandler.LEGACY_SETTINGS_KEY)) {
        return fail(
          "Namespace settings require valid JSON properties without duplicate names or the reserved namespaceSettings name.",
          "Validation"
        );
      }
      for (const propKey of Object.keys(obj)) {
        const keyValid = ConfigurationDocumentLocalFile.validateSegment(propKey, "key");
        if (!keyValid.ok) return keyValid;
      }
    }

    const doc = this._resolver.getDocument(namespace);
    const docPath = this._resolver.getDocumentPath(namespace);

    return doc.modify(async (current) => {
      const expectedExistingSha256 = PendingJournalManager.computeDocumentSha256(docPath);

      if (!isPlainObject) {
        const existingLegacy = current[NamespaceDocumentHandler.LEGACY_SETTINGS_KEY];
        const legacyProvider = this._resolver.normalizeProviderName(existingLegacy);
        if (existingLegacy !== undefined && legacyProvider !== "Default") {
          return fail(
            `configuration: '${namespace}/${NamespaceDocumentHandler.LEGACY_SETTINGS_KEY}' is held by '${legacyProvider}'; use move to change where it lives`,
            "Conflict"
          );
        }
        return ok({
          [NamespaceDocumentHandler.LEGACY_SETTINGS_KEY]: { value }
        });
      }

      const working: Record<string, ConfigurationValue> = { ...current };

      if (Object.prototype.hasOwnProperty.call(working, NamespaceDocumentHandler.LEGACY_SETTINGS_KEY)) {
        const legacyEntry = working[NamespaceDocumentHandler.LEGACY_SETTINGS_KEY]!;
        const legacyProvider = this._resolver.normalizeProviderName(legacyEntry);
        if (legacyProvider !== "Default") {
          return fail(
            `configuration: '${namespace}/${NamespaceDocumentHandler.LEGACY_SETTINGS_KEY}' is held by '${legacyProvider}'; use move to change where it lives`,
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
        delete working[NamespaceDocumentHandler.LEGACY_SETTINGS_KEY];
      }

      const incomingObj = value as Record<string, unknown>;
      const plannedDocument: Record<string, ConfigurationValue> = replace ? {} : { ...working };
      const vaultWrites: Array<{
        readonly provider: string;
        readonly secretName: string;
        readonly value: string;
      }> = [];

      for (const [propKey, propVal] of Object.entries(incomingObj)) {
        const existingEntry = working[propKey];
        const providerName = this._resolver.normalizeProviderName(existingEntry);

        const circularRes = this._resolver.checkCircularCredential(providerName, namespace, propKey);
        if (!circularRes.ok) return circularRes;

        const prepared = await this._resolver.prepareEntryForProvider(
          namespace,
          propKey,
          propVal,
          providerName,
          signal
        );
        if (!prepared.ok) return prepared;

        plannedDocument[propKey] = prepared.value.entry;
        if (prepared.value.vaultWrite) {
          vaultWrites.push(prepared.value.vaultWrite);
        }
      }

      const journalRes = this._resolver.journalManager.createWriteJournal(
        namespace,
        expectedExistingSha256,
        plannedDocument,
        vaultWrites
      );
      if (!journalRes.ok) return journalRes;
      const recoveryHandle = journalRes.value;

      for (const vw of vaultWrites) {
        signal?.throwIfAborted();
        const vault = this._resolver.vaultProviders.get(vw.provider);
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

      const stageRes = this._resolver.journalManager.updateWriteJournalStage(namespace, "local");
      if (!stageRes.ok) {
        return fail(stageRes.error, stageRes.category ?? "Unavailable", recoveryHandle);
      }

      const interceptRes = this._resolver.invokeWriteInterceptor(namespace);
      if (!interceptRes.ok) {
        return fail(interceptRes.error, interceptRes.category ?? "Unavailable", recoveryHandle);
      }

      this._resolver.journalManager.deleteWriteJournal(namespace);
      return ok(plannedDocument);
    }, signal);
  }
}
