// Responsibility: Safe two-phase provider migration (move) across plaintext, aesLocal, and external vault providers.
// Public interface:
// `-- ConfigurationMover
//     +-- move(namespace, key, destinationProvider, signal)
//     `-- new(resolver, recoveryCoordinator)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import type { ConfigurationEntryResolver } from "./ConfigurationEntryResolver.js";
import type { ConfigurationMove } from "./ConfigurationMove.js";
import type { ConfigurationRecoveryCoordinator } from "../recovery/ConfigurationRecoveryCoordinator.js";
import type {
  ConfigurationPublicationState,
  ConfigurationRecoveryState,
  ConfigurationRecoveryStep
} from "../recovery/Types.js";

/**
 * ConfigurationMover migrates a configuration entry between providers, writing to the destination
 * provider before updating the local namespace document and withdrawing the source copy.
 */
export class ConfigurationMover {
  private readonly _resolver: ConfigurationEntryResolver;
  private readonly _recovery: ConfigurationRecoveryCoordinator;

  public constructor(resolver: ConfigurationEntryResolver, recoveryCoordinator: ConfigurationRecoveryCoordinator) {
    this._resolver = resolver;
    this._recovery = recoveryCoordinator;
  }

  public async move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const targetProvider = this._resolver.canonicalProvider(destinationProvider);
    if (targetProvider === "oauth") {
      return fail("Cannot move an entry to the 'oauth' provider.", "Validation");
    }

    const circularRes = this._resolver.checkCircularCredential(targetProvider, namespace, key);
    if (!circularRes.ok) {
      return circularRes;
    }

    if (
      targetProvider !== "Default" &&
      targetProvider !== "aesLocal" &&
      !this._resolver.vaultProviders.has(targetProvider)
    ) {
      return fail(`Configuration provider '${targetProvider}' is not registered.`, "Validation");
    }

    const doc = this._resolver.getDocument(namespace);
    let wasNoOp = false;
    let stateSnapshot: ConfigurationRecoveryState | undefined;
    let publishing = false;

    const modRes = await doc.modify(async (current) => {
      const existing = current[key];
      if (!existing) {
        return fail(`No configuration entry exists at '${namespace}/${key}'.`, "NotFound");
      }

      const sourceProvider = this._resolver.normalizeProviderName(existing);
      if (sourceProvider === "oauth") {
        return fail("OAuth entries cannot be moved between providers.", "Conflict");
      }
      if (sourceProvider === targetProvider) {
        wasNoOp = true;
        return ok(current);
      }

      const clearRes = await this._resolver.resolveEntryValue(namespace, key, existing, signal);
      if (!clearRes.ok) {
        return clearRes;
      }

      const isJson = Boolean(existing.json) || typeof clearRes.value !== "string";
      const writeStepRes = await this._recovery.prepareStep(
        targetProvider,
        "Write",
        namespace,
        key,
        clearRes.value,
        undefined,
        isJson,
        signal
      );
      if (!writeStepRes.ok) {
        return writeStepRes;
      }

      const steps: ConfigurationRecoveryStep[] = [writeStepRes.value];
      if (sourceProvider !== "Default" && sourceProvider !== "aesLocal") {
        const remStepRes = await this._recovery.prepareStep(
          sourceProvider,
          "Remove",
          namespace,
          key,
          undefined,
          existing,
          isJson,
          signal
        );
        if (!remStepRes.ok) {
          return remStepRes;
        }
        steps.push(remStepRes.value);
      }

      const original = { ...current };
      const proposed = { ...current };
      const writeRes = await this._recovery.writeSteps(steps, proposed, signal);

      stateSnapshot = {
        operationId: crypto.randomUUID(),
        namespace,
        kind: "Move",
        original,
        proposed: { ...proposed },
        steps: [...steps],
        publication: "NotAttempted",
        conflicted: false,
        version: 1
      };

      if (!writeRes.ok) {
        return writeRes;
      }

      if (sourceProvider !== "Default" && sourceProvider !== "aesLocal") {
        const sourceSecretName =
          typeof existing.value === "string" ? existing.value : `${namespace}/${key}`;
        const sourceVault = this._resolver.vaultProviders.get(sourceProvider);
        if (sourceVault && sourceVault.recovery === undefined) {
          const moveJournalRes = this._resolver.journalManager.createMoveJournal(
            namespace,
            key,
            sourceProvider,
            sourceSecretName
          );
          if (!moveJournalRes.ok) {
            return moveJournalRes;
          }
        }
      }

      publishing = true;
      stateSnapshot = { ...stateSnapshot, publication: "Unknown" };

      const interceptRes = this._resolver.invokeWriteInterceptor(namespace);
      if (!interceptRes.ok) {
        return interceptRes;
      }

      current[key] = proposed[key]!;
      return ok(current);
    }, signal);

    if (!stateSnapshot) {
      if (modRes.ok) {
        return ok({ sourceValueRemains: false });
      }
      return modRes;
    }

    if (!modRes.ok) {
      const pubState: ConfigurationPublicationState = publishing ? "Failed" : "NotAttempted";
      const failedState: ConfigurationRecoveryState = {
        ...stateSnapshot,
        publication: pubState
      };
      const out = this._recovery.outcome(failedState);
      return fail(modRes.error, modRes.category ?? "Unavailable", out.recoveryHandle, out.progress);
    }

    if (wasNoOp) {
      return ok({ sourceValueRemains: false });
    }

    const stepsMut = [...stateSnapshot.steps];
    const cleanRes = await this._recovery.cleanupSteps(stepsMut, signal);
    this._resolver.journalManager.deleteMoveJournal(namespace);

    const publishedState: ConfigurationRecoveryState = {
      ...stateSnapshot,
      steps: stepsMut,
      publication: "Published"
    };
    const out = this._recovery.outcome(publishedState);
    const remSteps = stepsMut.filter((s) => s.operation.action === "Remove");
    const sourceValueRemains = remSteps.some((s) => s.state !== "Applied");
    const sourceDeleteError = !cleanRes.ok ? cleanRes.error : undefined;

    const moveVal: ConfigurationMove = {
      sourceValueRemains,
      ...(sourceDeleteError !== undefined ? { sourceDeleteError } : {}),
      ...(sourceValueRemains ? { progress: out.progress } : {}),
      ...(out.recoveryHandle !== undefined ? { recoveryHandle: out.recoveryHandle } : {})
    };
    return ok(
      moveVal,
      sourceValueRemains ? out.progress : undefined,
      out.recoveryHandle
    );
  }
}
