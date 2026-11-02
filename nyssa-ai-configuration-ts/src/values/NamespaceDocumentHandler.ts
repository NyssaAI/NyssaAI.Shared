// Responsibility: Whole-namespace get, set, and merge operations with legacy namespaceSettings migration and guarded two-phase recovery.
// Public interface:
// `-- NamespaceDocumentHandler
//     +-- getNamespace(namespace, signal)
//     +-- mergeSettings(namespace, value, signal)
//     +-- new(resolver, recoveryCoordinator)
//     `-- setNamespace(namespace, value, signal)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationDocumentLocalFile } from "./ConfigurationDocumentLocalFile.js";
import type { ConfigurationEntryResolver } from "./ConfigurationEntryResolver.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";
import type { ConfigurationRecoveryCoordinator } from "../recovery/ConfigurationRecoveryCoordinator.js";
import type {
  ConfigurationOperationKind,
  ConfigurationPublicationState,
  ConfigurationRecoveryState,
  ConfigurationRecoveryStep
} from "../recovery/Types.js";

/**
 * NamespaceDocumentHandler implements getNamespace, setNamespace, and mergeSettings
 * across multi-provider namespace documents with guarded two-phase recovery.
 */
export class NamespaceDocumentHandler {
  private static readonly LEGACY_SETTINGS_KEY = "namespaceSettings";
  private readonly _resolver: ConfigurationEntryResolver;
  private readonly _recovery: ConfigurationRecoveryCoordinator;

  public constructor(resolver: ConfigurationEntryResolver, recoveryCoordinator: ConfigurationRecoveryCoordinator) {
    this._resolver = resolver;
    this._recovery = recoveryCoordinator;
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

    const incomingEntries: Array<[string, unknown]> = [];
    if (isPlainObject) {
      const obj = value as Record<string, unknown>;
      if (Object.prototype.hasOwnProperty.call(obj, NamespaceDocumentHandler.LEGACY_SETTINGS_KEY)) {
        return fail(
          "Namespace settings require valid JSON properties without duplicate names or the reserved namespaceSettings name.",
          "Validation"
        );
      }
      for (const [propKey, propVal] of Object.entries(obj)) {
        if (propVal === undefined) continue;
        const keyValid = ConfigurationDocumentLocalFile.validateSegment(propKey, "key");
        if (!keyValid.ok) return keyValid;
        incomingEntries.push([propKey, propVal]);
      }
    }

    const doc = this._resolver.getDocument(namespace);
    let stateSnapshot: ConfigurationRecoveryState | undefined;
    let publishing = false;

    const modRes = await doc.modify(async (current) => {
      const original = { ...current };

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
          const legacyMerge = this.mergeLegacyProperties(legacyEntry, working);
          if (!legacyMerge.ok) return legacyMerge;
        }
        delete working[NamespaceDocumentHandler.LEGACY_SETTINGS_KEY];
      }

      const steps: ConfigurationRecoveryStep[] = [];
      for (const [propKey, propVal] of incomingEntries) {
        const existingEntry = working[propKey];
        const providerName = this._resolver.normalizeProviderName(existingEntry);

        const circularRes = this._resolver.checkCircularCredential(providerName, namespace, propKey);
        if (!circularRes.ok) return circularRes;

        const isJson = typeof propVal !== "string";
        const stepRes = await this._recovery.prepareStep(
          providerName,
          "Write",
          namespace,
          propKey,
          propVal,
          existingEntry,
          isJson,
          signal
        );
        if (!stepRes.ok) return stepRes;
        steps.push(stepRes.value);
      }

      const proposed: Record<string, ConfigurationValue> = replace ? {} : { ...working };
      const kind: ConfigurationOperationKind = replace ? "Replace" : "Merge";
      const opId = crypto.randomUUID();
      const written = await this._recovery.writeSteps(steps, proposed, signal);

      stateSnapshot = {
        operationId: opId,
        namespace,
        kind,
        original,
        proposed: { ...proposed },
        steps: [...steps],
        publication: "NotAttempted",
        conflicted: false,
        version: 1
      };

      if (!written.ok) {
        return written;
      }

      publishing = true;
      stateSnapshot = { ...stateSnapshot, publication: "Unknown" };

      const interceptRes = this._resolver.invokeWriteInterceptor(namespace);
      if (!interceptRes.ok) {
        return interceptRes;
      }

      return ok(proposed);
    }, signal);

    if (!stateSnapshot) {
      return modRes;
    }

    const pubState: ConfigurationPublicationState = modRes.ok
      ? "Published"
      : publishing
        ? "Failed"
        : "NotAttempted";
    const finalState: ConfigurationRecoveryState = {
      ...stateSnapshot,
      publication: pubState
    };
    const out = this._recovery.outcome(finalState);

    if (modRes.ok) {
      return ok(undefined, out.progress, out.recoveryHandle);
    }
    return fail(modRes.error, modRes.category ?? "Unavailable", out.recoveryHandle, out.progress);
  }

  private mergeLegacyProperties(
    legacyEntry: ConfigurationValue,
    working: Record<string, ConfigurationValue>
  ): Result<void> {
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
    return ok(undefined);
  }
}
