// Responsibility: Coordinate two-phase prepare, execute, reconcile, and recover operations across local documents and cloud vaults.
// Public interface:
// `-- ConfigurationRecoveryCoordinator
//     +-- cleanupSteps(steps, signal)
//     +-- new(resolver, protector)
//     +-- outcome(state)
//     +-- prepareStep(providerName, action, namespace, key, value, source, isJson, signal)
//     +-- recover(handle, signal)
//     `-- writeSteps(steps, proposed, signal)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import type { ConfigurationEntryResolver } from "../values/ConfigurationEntryResolver.js";
import type { ConfigurationRecoveryTokenProtector } from "./ConfigurationRecoveryTokenProtector.js";
import type { ConfigurationRecoveryHandle } from "./ConfigurationRecoveryHandle.js";
import type {
  ConfigurationOperationProgress,
  ConfigurationPublicationState,
  ConfigurationRecoveryOutcome,
  ConfigurationRecoveryState,
  ConfigurationRecoveryStatus,
  ConfigurationRecoveryStep
} from "./Types.js";
import type {
  CloudKeyVaultAction,
  CloudKeyVaultOperation,
  CloudKeyVaultPreparation,
  ConfigurationPointer
} from "../vault/Types.js";
import type { ConfigurationValue } from "../values/ConfigurationValue.js";

const CONFLICT_MSG = "Configuration changed after this operation. Recovery stopped.";
const MANUAL_MSG = "The provider cannot establish safe recovery. Reconcile the recorded operation manually.";
const UNFINISHED_MSG = "The configuration operation is unfinished.";

/**
 * ConfigurationRecoveryCoordinator executes guarded two-phase multi-step operations and resumes interrupted handles.
 */
export class ConfigurationRecoveryCoordinator {
  private readonly _resolver: ConfigurationEntryResolver;
  private readonly _protector: ConfigurationRecoveryTokenProtector | undefined;

  public constructor(resolver: ConfigurationEntryResolver, protector?: ConfigurationRecoveryTokenProtector) {
    this._resolver = resolver;
    this._protector = protector;
  }

  public async prepareStep(
    providerName: string, action: CloudKeyVaultAction, namespace: string, key: string,
    value: unknown, source: ConfigurationValue | undefined, isJson: boolean, signal?: AbortSignal
  ): Promise<Result<ConfigurationRecoveryStep>> {
    signal?.throwIfAborted();
    const canonical = this._resolver.canonicalProvider(providerName);
    const isLocal = canonical === "Default" || canonical === "aesLocal" || canonical === "oauth";
    const strValue = value === undefined ? undefined : typeof value === "string" && !(isLocal && canonical === "Default" && isJson) ? value : JSON.stringify(value);
    const operationId = crypto.randomUUID();

    if (!isLocal) {
      const vault = this._resolver.vaultProviders.get(canonical);
      if (!vault) return fail(`Configuration provider '${canonical}' is not registered.`, "Validation");
      const pointer: ConfigurationPointer | undefined =
        source !== undefined && typeof source.value === "string" ? { provider: canonical, value: source.value } : undefined;
      const op: CloudKeyVaultOperation = {
        operationId,
        action,
        namespace,
        key,
        ...(strValue !== undefined ? { value: strValue } : {}),
        ...(pointer !== undefined ? { pointer } : {})
      };
      let preparation: CloudKeyVaultPreparation | undefined;
      if (vault.recovery !== undefined) {
        const prepRes = await vault.recovery.prepare(op, signal);
        signal?.throwIfAborted();
        if (!prepRes.ok) return prepRes;
        if (!prepRes.value.storeIdentity.trim() || prepRes.value.storeIdentity !== vault.recovery.storeIdentity) {
          return fail(CONFLICT_MSG, "Conflict");
        }
        preparation = prepRes.value;
      }
      const targetReference =
        preparation?.targetReference ??
        (source !== undefined && typeof source.value === "string" ? source.value : `${namespace}/${key}`);
      return ok({
        provider: canonical,
        operation: op,
        local: false,
        isJsonValue: isJson,
        ...(preparation !== undefined ? { preparation } : {}),
        targetReference,
        ...(source !== undefined ? { source } : {}),
        state: "Pending"
      });
    }

    return ok({
      provider: canonical === "Default" ? "" : canonical,
      operation: { operationId, action, namespace, key, ...(strValue !== undefined ? { value: strValue } : {}) },
      local: true,
      isJsonValue: isJson,
      ...(source !== undefined ? { source } : {}),
      state: "Pending"
    });
  }

  public async writeSteps(
    steps: ConfigurationRecoveryStep[],
    proposed: Record<string, ConfigurationValue>,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    for (let idx = 0; idx < steps.length; idx++) {
      const step = steps[idx]!;
      if (step.operation.action !== "Write") continue;
      if (!ConfigurationRecoveryCoordinator.isTerminal(step)) {
        const runRes = await this.runStep(steps, idx, false, signal);
        if (!runRes.ok) return runRes;
      }
      const outEntry = steps[idx]!.output;
      if (outEntry !== undefined) proposed[step.operation.key] = outEntry;
    }
    return ok(undefined);
  }

  public async cleanupSteps(steps: ConfigurationRecoveryStep[], signal?: AbortSignal): Promise<Result<void>> {
    for (let idx = 0; idx < steps.length; idx++) {
      const step = steps[idx]!;
      if (step.operation.action === "Remove" && !ConfigurationRecoveryCoordinator.isTerminal(step)) {
        const runRes = await this.runStep(steps, idx, false, signal);
        if (!runRes.ok) return runRes;
      }
    }
    return ok(undefined);
  }

  public outcome(state: ConfigurationRecoveryState): ConfigurationRecoveryOutcome {
    const status = ConfigurationRecoveryCoordinator.status(state);
    const progress = ConfigurationRecoveryCoordinator.progress(state, status);
    let recoveryHandle: ConfigurationRecoveryHandle | undefined;
    if (this._protector !== undefined && status === "Unfinished") {
      const sealed = this._protector.seal(state);
      if (sealed.ok) recoveryHandle = sealed.value;
    }
    return { status, progress, ...(recoveryHandle !== undefined ? { recoveryHandle } : {}) };
  }

  public async recover(
    handle: ConfigurationRecoveryHandle,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationRecoveryOutcome>> {
    signal?.throwIfAborted();
    if (!this._protector) return fail(MANUAL_MSG, "Indeterminate");
    const opened = this._protector.open(handle);
    if (!opened.ok) return opened;

    const state = opened.value;
    const doc = this._resolver.getDocument(state.namespace);
    const steps: ConfigurationRecoveryStep[] = [...state.steps];
    const proposed: Record<string, ConfigurationValue> = { ...state.proposed };
    let publication: ConfigurationPublicationState = state.publication;
    let conflicted = state.conflicted;
    let pubStarted = false;

    const markConflict = (): Result<Record<string, ConfigurationValue>> => {
      conflicted = true;
      if (steps.length > 0) steps[0] = { ...steps[0]!, state: "Conflict" };
      return fail(CONFLICT_MSG, "Conflict");
    };

    const modRes = await doc.modify(async (entries) => {
      const isOrig = ConfigurationRecoveryCoordinator.matches(entries, state.original);
      const allWritesTerminal = () =>
        steps.filter((s) => s.operation.action === "Write").every(ConfigurationRecoveryCoordinator.isTerminal);
      let isProp = ConfigurationRecoveryCoordinator.matches(entries, proposed) && allWritesTerminal();

      if (publication === "Published" && !isProp) return markConflict();
      if (isProp && (!isOrig || publication === "Published")) publication = "Published";

      const relevant = publication === "Published" ? steps.filter((s) => s.operation.action === "Remove") : steps;
      for (const s of relevant) {
        if (!s.local) {
          const vault = this._resolver.vaultProviders.get(s.provider);
          if (!vault || (s.preparation !== undefined && vault.recovery?.storeIdentity !== s.preparation.storeIdentity)) {
            return markConflict();
          }
        }
      }

      for (let idx = 0; idx < steps.length; idx++) {
        const s = steps[idx]!;
        if (!s.local && s.state !== "Retained" && !(publication === "Published" && s.operation.action === "Write")) {
          const recRes = await this.runStep(steps, idx, true, signal);
          if (!recRes.ok) return recRes;
        }
      }

      for (const s of steps) {
        if (s.operation.action === "Write" && s.output !== undefined) proposed[s.operation.key] = s.output;
      }

      isProp = ConfigurationRecoveryCoordinator.matches(entries, proposed) && allWritesTerminal();
      if (!isOrig && !isProp) return markConflict();
      if (isProp) publication = "Published";

      if (publication !== "Published") {
        const wRes = await this.writeSteps(steps, proposed, signal);
        if (!wRes.ok) return wRes;
        const interceptRes = this._resolver.invokeWriteInterceptor(state.namespace);
        if (!interceptRes.ok) return interceptRes;
        pubStarted = true;
        publication = "Unknown";
        return ok({ ...proposed });
      }

      const cRes = await this.cleanupSteps(steps, signal);
      return cRes.ok ? ok(entries) : cRes;
    }, signal);

    if (modRes.ok) {
      if (pubStarted) publication = "Published";
      await this.cleanupSteps(steps, signal);
    } else if (pubStarted) {
      publication = "Failed";
    }

    return ok(this.outcome({ ...state, proposed, steps, publication, conflicted }));
  }

  private async runStep(
    steps: ConfigurationRecoveryStep[],
    index: number,
    reconcile: boolean,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    const step = steps[index]!;
    const req = step.operation;
    if (step.local) return this.runLocalStep(steps, index);

    const vault = this._resolver.vaultProviders.get(step.provider);
    if (!vault) return fail(`Configuration provider '${step.provider}' is not registered.`, "Validation");

    if (step.preparation !== undefined) {
      const cap = vault.recovery;
      if (!cap || cap.storeIdentity !== step.preparation.storeIdentity) {
        steps[index] = { ...step, state: "Conflict" };
        return fail(CONFLICT_MSG, "Conflict");
      }
      steps[index] = { ...step, state: "Unknown" };
      const effRes = reconcile ? await cap.reconcile(req, step.preparation, signal) : await cap.execute(req, step.preparation, signal);
      signal?.throwIfAborted();
      if (!effRes.ok) return effRes;
      const eff = effRes.value;
      if (reconcile && ConfigurationRecoveryCoordinator.isTerminal(step) && eff.state === "Failed") {
        steps[index] = { ...step, state: "Manual" };
        return fail(MANUAL_MSG, "Indeterminate");
      }
      let output = step.output;
      if (eff.state === "Applied" && req.action === "Write") {
        if (!eff.pointer || eff.pointer.provider !== step.provider) return fail(UNFINISHED_MSG, "Unavailable");
        if (output !== undefined && output.value !== eff.pointer.value) {
          steps[index] = { ...step, state: "Conflict" };
          return fail(CONFLICT_MSG, "Conflict");
        }
        output = { value: eff.pointer.value, provider: step.provider, ...(step.isJsonValue ? { json: true } : {}) };
      }
      steps[index] = { ...step, state: eff.state, ...(output !== undefined ? { output } : {}) };
      if (eff.state === "Applied" || eff.state === "Retained" || (reconcile && eff.state === "Failed")) return ok(undefined);
      if (eff.state === "Conflict") return fail(CONFLICT_MSG, "Conflict");
      return eff.state === "Manual" ? fail(MANUAL_MSG, "Indeterminate") : fail(UNFINISHED_MSG, "Unavailable");
    }

    if (reconcile) {
      steps[index] = { ...step, state: "Manual" };
      return fail(MANUAL_MSG, "Indeterminate");
    }
    steps[index] = { ...step, state: "Unknown" };
    const secretName = `${req.namespace}/${req.key}`;
    if (req.action === "Write") {
      const wRes = await vault.setSecret(secretName, req.value ?? "", signal);
      if (!wRes.ok) return wRes;
      steps[index] = { ...step, state: "Applied", output: { value: secretName, provider: step.provider, ...(step.isJsonValue ? { json: true } : {}) } };
      return ok(undefined);
    }
    const srcName = step.source && typeof step.source.value === "string" ? step.source.value : secretName;
    const rRes = await vault.deleteSecret(srcName, signal);
    if (!rRes.ok) return rRes;
    steps[index] = { ...step, state: rRes.value.retained ? "Retained" : "Applied" };
    return ok(undefined);
  }

  private runLocalStep(steps: ConfigurationRecoveryStep[], index: number): Result<void> {
    const step = steps[index]!;
    const req = step.operation;
    if (req.action === "Remove") {
      steps[index] = { ...step, state: "Applied" };
      return ok(undefined);
    }
    const rawVal = req.value ?? "";
    if (step.provider === "" || step.provider === "Default") {
      let parsedVal: unknown = rawVal;
      if (step.isJsonValue) { try { parsedVal = JSON.parse(rawVal); } catch { parsedVal = rawVal; } }
      steps[index] = { ...step, state: "Applied", output: { value: parsedVal } };
      return ok(undefined);
    }
    const cipherRes = this._resolver.sealLocalValue(rawVal, req.namespace, req.key, step.isJsonValue);
    if (!cipherRes.ok) {
      steps[index] = { ...step, state: "Failed" };
      return cipherRes;
    }
    steps[index] = { ...step, state: "Applied", output: cipherRes.value };
    return ok(undefined);
  }

  private static isTerminal(step: ConfigurationRecoveryStep): boolean {
    return step.state === "Applied" || step.state === "Retained";
  }

  private static status(state: ConfigurationRecoveryState): ConfigurationRecoveryStatus {
    if (state.conflicted) return "Conflict";
    const relevant = state.publication === "Published" ? state.steps.filter((s) => s.operation.action === "Remove") : state.steps;
    if (relevant.some((s) => s.state === "Conflict")) return "Conflict";
    if (relevant.some((s) => s.state === "Manual" || (!s.local && s.preparation === undefined && !this.isTerminal(s)))) return "ManualReconciliation";
    if (state.publication === "Published" && relevant.every((s) => this.isTerminal(s))) return "Completed";
    return relevant.some((s) => !s.local && s.preparation === undefined) ? "ManualReconciliation" : "Unfinished";
  }

  private static progress(state: ConfigurationRecoveryState, status: ConfigurationRecoveryStatus): ConfigurationOperationProgress {
    return {
      operationId: state.operationId,
      namespace: state.namespace,
      kind: state.kind,
      publication: state.publication,
      recovery: status,
      steps: state.steps.map((s) => ({
        operationId: s.operation.operationId, key: s.operation.key, provider: s.provider,
        action: s.operation.action, state: s.state, ...(s.targetReference !== undefined ? { targetReference: s.targetReference } : {})
      }))
    };
  }

  private static matches(actual: Readonly<Record<string, ConfigurationValue>>, expected: Readonly<Record<string, ConfigurationValue>>): boolean {
    const actKeys = Object.keys(actual);
    const expKeys = Object.keys(expected);
    return actKeys.length === expKeys.length && expKeys.every((k) => Object.prototype.hasOwnProperty.call(actual, k) && JSON.stringify(actual[k]) === JSON.stringify(expected[k]));
  }
}
