// Responsibility: Derive HKDF-SHA256 purpose subkeys and seal/authenticate ConfigurationRecoveryHandle tokens.
// Public interface:
// `-- ConfigurationRecoveryTokenProtector
//     +-- canonicalRoot(root)
//     +-- new(applicationKey, applicationName, configurationDirectory)
//     +-- open(handle)
//     `-- seal(state)

import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationCipherAesGcm } from "../encryption/ConfigurationCipherAesGcm.js";
import { ConfigurationRecoveryHandle } from "./ConfigurationRecoveryHandle.js";
import type {
  ConfigurationOperationKind,
  ConfigurationPublicationState,
  ConfigurationRecoveryState,
  ConfigurationRecoveryStep
} from "./Types.js";
import type {
  CloudKeyVaultAction,
  CloudKeyVaultOperation,
  CloudKeyVaultPreparation,
  ConfigurationPointer,
  ConfigurationStepState
} from "../vault/Types.js";
import type { ConfigurationValue } from "../values/ConfigurationValue.js";

const HKDF_INFO = Buffer.from("NyssaAI.Configuration.Recovery/v1", "utf8");
const INVALID_HANDLE_MSG =
  "The recovery handle is invalid, unsupported, or belongs to a different application, configuration root, or protection key.";
const KINDS: readonly ConfigurationOperationKind[] = ["Move", "Replace", "Merge"];
const PUBLICATIONS: readonly ConfigurationPublicationState[] = ["NotAttempted", "Published", "Failed", "Unknown"];
const ACTIONS: readonly CloudKeyVaultAction[] = ["Write", "Remove"];
const STEP_STATES: readonly ConfigurationStepState[] = [
  "Pending",
  "Applied",
  "Failed",
  "Unknown",
  "Conflict",
  "Retained",
  "Manual"
];

/**
 * ConfigurationRecoveryTokenProtector encrypts and authenticates recovery state with a purpose-specific HKDF-SHA256 subkey.
 */
export class ConfigurationRecoveryTokenProtector {
  private readonly _cipher: ConfigurationCipherAesGcm;
  private readonly _application: string;
  private readonly _root: string;

  public constructor(
    applicationKey: Buffer | Uint8Array,
    applicationName: string,
    configurationDirectory: string
  ) {
    const derived = Buffer.from(
      crypto.hkdfSync("sha256", Buffer.from(applicationKey), Buffer.alloc(0), HKDF_INFO, 32)
    );
    const cipherRes = ConfigurationCipherAesGcm.create(derived);
    derived.fill(0);
    if (!cipherRes.ok) {
      throw new globalThis.Error(cipherRes.message);
    }
    this._cipher = cipherRes.value;
    this._application = applicationName;
    this._root = ConfigurationRecoveryTokenProtector.canonicalRoot(configurationDirectory);
  }

  /**
   * Resolves symlinks, trims trailing separators, and uppercases on Windows to match .NET CanonicalRoot.
   */
  public static canonicalRoot(root: string): string {
    let resolved = path.resolve(root);
    try {
      if (fs.existsSync(resolved)) {
        resolved = fs.realpathSync(resolved);
      }
    } catch {
      // keep resolved path
    }
    resolved = resolved.replace(/[/\\]+$/, "");
    return process.platform === "win32" ? resolved.toUpperCase() : resolved;
  }

  public seal(state: ConfigurationRecoveryState): Result<ConfigurationRecoveryHandle> {
    const serialized = ConfigurationRecoveryTokenProtector.encodeState(state);
    const encrypted = this._cipher.seal(serialized, this._application, this._root);
    if (!encrypted.ok) {
      return encrypted;
    }
    return ConfigurationRecoveryHandle.import(encrypted.value);
  }

  public open(handle: ConfigurationRecoveryHandle): Result<ConfigurationRecoveryState> {
    const opened = this._cipher.open(handle.export(), this._application, this._root);
    if (!opened.ok) {
      return fail(INVALID_HANDLE_MSG, "Validation");
    }
    return ConfigurationRecoveryTokenProtector.decodeState(opened.value);
  }

  private static encodeState(state: ConfigurationRecoveryState): string {
    const stepsList = state.steps.map((s) => ({
      Provider: s.provider,
      Operation: {
        OperationId: s.operation.operationId,
        Action: ACTIONS.indexOf(s.operation.action),
        Namespace: s.operation.namespace,
        Key: s.operation.key,
        Value: s.operation.value ?? null,
        Pointer: s.operation.pointer
          ? { Provider: s.operation.pointer.provider, Value: s.operation.pointer.value }
          : null
      },
      Local: s.local,
      IsJsonValue: s.isJsonValue,
      Preparation: s.preparation
        ? {
            StoreIdentity: s.preparation.storeIdentity,
            TargetReference: s.preparation.targetReference ?? null,
            Precondition: s.preparation.precondition ?? null
          }
        : null,
      TargetReference: s.targetReference ?? null,
      Output: s.output ?? null,
      Source: s.source ?? null,
      State: STEP_STATES.indexOf(s.state)
    }));

    const payload = {
      Conflicted: state.conflicted,
      Version: state.version,
      OperationId: state.operationId,
      Namespace: state.namespace,
      Kind: KINDS.indexOf(state.kind),
      Original: state.original,
      Proposed: state.proposed,
      Steps: stepsList,
      Publication: PUBLICATIONS.indexOf(state.publication)
    };
    return JSON.stringify(payload);
  }

  private static decodeState(rawJson: string): Result<ConfigurationRecoveryState> {
    let parsed: unknown;
    try {
      parsed = JSON.parse(rawJson);
    } catch {
      return fail(INVALID_HANDLE_MSG, "Validation");
    }
    if (!this.isRecord(parsed)) {
      return fail(INVALID_HANDLE_MSG, "Validation");
    }
    const ver = parsed["Version"] ?? parsed["version"];
    if (ver !== 1) {
      return fail(INVALID_HANDLE_MSG, "Validation");
    }

    const origRaw = parsed["Original"] ?? parsed["original"];
    const propRaw = parsed["Proposed"] ?? parsed["proposed"];
    const stepsRaw = parsed["Steps"] ?? parsed["steps"];
    const opId = parsed["OperationId"] ?? parsed["operationId"];
    const ns = parsed["Namespace"] ?? parsed["namespace"];
    const kindRaw = parsed["Kind"] ?? parsed["kind"];
    const pubRaw = parsed["Publication"] ?? parsed["publication"] ?? 0;

    const kind =
      typeof kindRaw === "number" && kindRaw >= 0 && kindRaw < KINDS.length
        ? KINDS[kindRaw]
        : typeof kindRaw === "string" && (KINDS as readonly string[]).includes(kindRaw)
          ? (kindRaw as ConfigurationOperationKind)
          : undefined;
    const pub =
      typeof pubRaw === "number" && pubRaw >= 0 && pubRaw < PUBLICATIONS.length
        ? PUBLICATIONS[pubRaw]
        : typeof pubRaw === "string" && (PUBLICATIONS as readonly string[]).includes(pubRaw)
          ? (pubRaw as ConfigurationPublicationState)
          : undefined;

    if (
      !this.isRecord(origRaw) ||
      !this.isRecord(propRaw) ||
      !Array.isArray(stepsRaw) ||
      typeof opId !== "string" ||
      typeof ns !== "string" ||
      !kind ||
      !pub
    ) {
      return fail(INVALID_HANDLE_MSG, "Validation");
    }

    const origMap: Record<string, ConfigurationValue> = {};
    for (const [k, v] of Object.entries(origRaw)) {
      const dec = this.decodeEntry(v);
      if (!dec.ok) return fail(INVALID_HANDLE_MSG, "Validation");
      origMap[k] = dec.value;
    }

    const propMap: Record<string, ConfigurationValue> = {};
    for (const [k, v] of Object.entries(propRaw)) {
      const dec = this.decodeEntry(v);
      if (!dec.ok) return fail(INVALID_HANDLE_MSG, "Validation");
      propMap[k] = dec.value;
    }

    const stepsList: ConfigurationRecoveryStep[] = [];
    for (const item of stepsRaw) {
      const stepRes = this.decodeStep(item);
      if (!stepRes.ok) return fail(INVALID_HANDLE_MSG, "Validation");
      stepsList.push(stepRes.value);
    }

    return ok({
      operationId: opId,
      namespace: ns,
      kind,
      original: origMap,
      proposed: propMap,
      steps: stepsList,
      publication: pub,
      conflicted: Boolean(parsed["Conflicted"] ?? parsed["conflicted"] ?? false),
      version: 1
    });
  }

  private static decodeStep(item: unknown): Result<ConfigurationRecoveryStep> {
    if (!this.isRecord(item)) return fail(INVALID_HANDLE_MSG, "Validation");
    const opRaw = item["Operation"] ?? item["operation"];
    if (!this.isRecord(opRaw)) return fail(INVALID_HANDLE_MSG, "Validation");

    const ptrRaw = opRaw["Pointer"] ?? opRaw["pointer"];
    let pointer: ConfigurationPointer | undefined;
    if (this.isRecord(ptrRaw)) {
      pointer = {
        provider: String(ptrRaw["Provider"] ?? ptrRaw["provider"] ?? ""),
        value: String(ptrRaw["Value"] ?? ptrRaw["value"] ?? "")
      };
    }

    const actRaw = opRaw["Action"] ?? opRaw["action"] ?? 0;
    const action: CloudKeyVaultAction =
      typeof actRaw === "number" && actRaw >= 0 && actRaw < ACTIONS.length
        ? ACTIONS[actRaw]!
        : actRaw === "Remove"
          ? "Remove"
          : "Write";

    const valRaw = opRaw["Value"] ?? opRaw["value"];
    const operation: CloudKeyVaultOperation = {
      operationId: String(opRaw["OperationId"] ?? opRaw["operationId"] ?? ""),
      action,
      namespace: String(opRaw["Namespace"] ?? opRaw["namespace"] ?? ""),
      key: String(opRaw["Key"] ?? opRaw["key"] ?? ""),
      ...(typeof valRaw === "string" ? { value: valRaw } : {}),
      ...(pointer !== undefined ? { pointer } : {})
    };

    const prepRaw = item["Preparation"] ?? item["preparation"];
    let preparation: CloudKeyVaultPreparation | undefined;
    if (this.isRecord(prepRaw)) {
      const tRef = prepRaw["TargetReference"] ?? prepRaw["targetReference"];
      const prec = prepRaw["Precondition"] ?? prepRaw["precondition"];
      preparation = {
        storeIdentity: String(prepRaw["StoreIdentity"] ?? prepRaw["storeIdentity"] ?? ""),
        ...(typeof tRef === "string" ? { targetReference: tRef } : {}),
        ...(typeof prec === "string" ? { precondition: prec } : {})
      };
    }

    const outRaw = item["Output"] ?? item["output"];
    let output: ConfigurationValue | undefined;
    if (outRaw !== null && outRaw !== undefined) {
      const decOut = this.decodeEntry(outRaw);
      if (!decOut.ok) return decOut;
      output = decOut.value;
    }

    const srcRaw = item["Source"] ?? item["source"];
    let source: ConfigurationValue | undefined;
    if (srcRaw !== null && srcRaw !== undefined) {
      const decSrc = this.decodeEntry(srcRaw);
      if (!decSrc.ok) return decSrc;
      source = decSrc.value;
    }

    const stRaw = item["State"] ?? item["state"] ?? 0;
    const state: ConfigurationStepState =
      typeof stRaw === "number" && stRaw >= 0 && stRaw < STEP_STATES.length
        ? STEP_STATES[stRaw]!
        : typeof stRaw === "string" && (STEP_STATES as readonly string[]).includes(stRaw)
          ? (stRaw as ConfigurationStepState)
          : "Pending";

    const stepTRef = item["TargetReference"] ?? item["targetReference"];
    return ok({
      provider: String(item["Provider"] ?? item["provider"] ?? ""),
      operation,
      local: Boolean(item["Local"] ?? item["local"] ?? false),
      isJsonValue: Boolean(item["IsJsonValue"] ?? item["isJsonValue"] ?? false),
      ...(preparation !== undefined ? { preparation } : {}),
      ...(typeof stepTRef === "string" ? { targetReference: stepTRef } : {}),
      ...(output !== undefined ? { output } : {}),
      ...(source !== undefined ? { source } : {}),
      state
    });
  }

  private static decodeEntry(raw: unknown): Result<ConfigurationValue> {
    if (!this.isRecord(raw) || !("value" in raw)) {
      return fail(INVALID_HANDLE_MSG, "Validation");
    }
    const provider = typeof raw["provider"] === "string" && raw["provider"].length > 0 ? raw["provider"] : undefined;
    const encrypted = raw["encrypted"] === true ? true : undefined;
    const json = raw["json"] === true ? true : undefined;
    const reauth = raw["reauth_required"] === true ? true : undefined;
    return ok({
      value: raw["value"],
      ...(encrypted !== undefined ? { encrypted } : {}),
      ...(provider !== undefined ? { provider } : {}),
      ...(json !== undefined ? { json } : {}),
      ...(reauth !== undefined ? { reauth_required: reauth } : {})
    });
  }

  private static isRecord(val: unknown): val is Record<string, unknown> {
    return typeof val === "object" && val !== null && !Array.isArray(val);
  }
}
