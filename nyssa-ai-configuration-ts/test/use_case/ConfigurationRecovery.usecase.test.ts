import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { ConfigurationRecoveryHandle } from "../../src/recovery/ConfigurationRecoveryHandle.js";
import { ok, fail, type Result } from "@nyssa-ai/result";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "../../src/vault/IVaultProviderFactory.js";
import type { CloudKeyVaultRecovery } from "../../src/vault/CloudKeyVaultRecovery.js";
import type {
  CloudKeyVaultOperation,
  CloudKeyVaultPreparation,
  CloudKeyVaultStepEffect,
  ConfigurationPointer
} from "../../src/vault/Types.js";

class GuardedVault {
  public readonly name = "bitwarden";
  public readonly storeIdentity: string;
  public readonly items = new Map<string, string>();
  public readonly executedOps = new Map<string, ConfigurationPointer | null>();
  public failOperationKey?: string | undefined;

  public constructor(storeIdentity: string = "vault-instance-01") {
    this.storeIdentity = storeIdentity;
  }

  public get recovery(): CloudKeyVaultRecovery {
    return this;
  }

  public prepare(operation: CloudKeyVaultOperation): Result<CloudKeyVaultPreparation> {
    const ref =
      operation.pointer !== undefined
        ? operation.pointer.value
        : `guarded://${operation.namespace}/${operation.key}`;
    return ok({
      storeIdentity: this.storeIdentity,
      targetReference: ref
    });
  }

  public execute(
    operation: CloudKeyVaultOperation,
    preparation: CloudKeyVaultPreparation
  ): Result<CloudKeyVaultStepEffect> {
    if (this.failOperationKey === operation.key) {
      return fail(`Simulated failure executing step for '${operation.key}'.`, "Unavailable");
    }
    const ref = preparation.targetReference ?? `guarded://${operation.namespace}/${operation.key}`;
    if (operation.action === "Write") {
      this.items.set(ref, operation.value ?? "");
      const ptr: ConfigurationPointer = { provider: "bitwarden", value: ref };
      this.executedOps.set(operation.operationId, ptr);
      return ok({ state: "Applied", pointer: ptr });
    }
    this.items.delete(ref);
    this.executedOps.set(operation.operationId, null);
    return ok({ state: "Applied" });
  }

  public reconcile(
    operation: CloudKeyVaultOperation,
    _preparation: CloudKeyVaultPreparation
  ): Result<CloudKeyVaultStepEffect> {
    if (this.executedOps.has(operation.operationId)) {
      const ptr = this.executedOps.get(operation.operationId) ?? undefined;
      return ok({
        state: "Applied",
        ...(ptr !== undefined ? { pointer: ptr } : {})
      });
    }
    return ok({ state: "Failed" });
  }

  public async getSecret(secretName: string): Promise<Result<string>> {
    const val = this.items.get(secretName);
    if (val === undefined) return fail(`Secret '${secretName}' not found.`, "NotFound");
    return ok(val);
  }

  public async setSecret(secretName: string, secretValue: string): Promise<Result<void>> {
    const ref = secretName.startsWith("guarded://") ? secretName : `guarded://${secretName}`;
    this.items.set(ref, secretValue);
    this.items.set(secretName, secretValue);
    return ok(undefined);
  }

  public async deleteSecret(
    secretName: string
  ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
    this.items.delete(secretName);
    return ok({ deleted: true, retained: false });
  }
}

describe("ConfigurationRecovery Public Use Cases (F-204)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  let keyring: InMemoryKeyring;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-recovery-test-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
    configDir = path.join(tempDir, "config");
    keyring = new InMemoryKeyring();
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("configurationRecovery_interruptedMove_returnsRecoveryHandleAndRecoversToCompleted (F-204 UC-001 & UC-002)", async () => {
    const vault = new GuardedVault();
    const factory: IVaultProviderFactory = {
      kind: "bitwarden",
      create(): Result<IVaultProvider> {
        return ok(vault);
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "nyssa-app",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "bitwarden",
        kind: "bitwarden"
      },
      vaultFactories: [factory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect(
      (
        await client.set("payments", "secretKey", "sk_live_abc123", {
          encrypted: true,
          provider: "aesLocal"
        })
      ).ok
    ).toBe(true);

    // Simulate a local disk write failure after the remote vault write succeeds
    client.setDocumentWriteInterceptor((ns) => fail(`Simulated crash publishing ${ns}.json`, "Unavailable"));
    const attempt = await client.move("payments", "secretKey", "bitwarden");

    expect(attempt.ok).toBe(false);
    if (attempt.ok) return;
    expect(attempt.progress).toBeDefined();
    expect(attempt.progress?.recovery).toBe("Unfinished");
    expect(attempt.recoveryHandle).toBeDefined();

    // Export and re-import the opaque recovery token across process boundaries
    const exportedToken = attempt.recoveryHandle!.export();
    expect(attempt.recoveryHandle!.toString()).toBe("ConfigurationRecoveryHandle { protected }");
    expect(attempt.recoveryHandle!.toString()).not.toContain(exportedToken);

    const reimported = ConfigurationRecoveryHandle.import(exportedToken);
    expect(reimported.ok).toBe(true);
    if (!reimported.ok) return;

    // Restore local disk writes and recover
    client.setDocumentWriteInterceptor(undefined);
    const recovered = await client.recover(reimported.value);
    expect(recovered.ok).toBe(true);
    if (!recovered.ok) return;
    expect(recovered.value.status).toBe("Completed");
    expect(recovered.value.recoveryHandle).toBeUndefined();

    const valRes = await client.get("payments", "secretKey");
    expect(valRes.ok && valRes.value).toBe("sk_live_abc123");
  });

  it("configurationRecovery_interruptedSetNamespace_recoversAndDetectsConcurrentConflict (F-204 UC-003)", async () => {
    const vault = new GuardedVault();
    const factory: IVaultProviderFactory = {
      kind: "bitwarden",
      create(): Result<IVaultProvider> {
        return ok(vault);
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "nyssa-app",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "bitwarden",
        kind: "bitwarden"
      },
      vaultFactories: [factory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect(
      (
        await client.set("payments", "vaultKey", "v1", {
          provider: "bitwarden"
        })
      ).ok
    ).toBe(true);
    expect((await client.set("payments", "region", "us-west")).ok).toBe(true);

    client.setDocumentWriteInterceptor((ns) => fail(`Crash writing ${ns}`, "Unavailable"));
    const interrupted = await client.setNamespace("payments", {
      vaultKey: "v2",
      region: "eu-central"
    });
    expect(interrupted.ok).toBe(false);
    if (interrupted.ok) return;
    expect(interrupted.recoveryHandle).toBeDefined();

    // External modification occurs before recover() is called
    client.setDocumentWriteInterceptor(undefined);
    expect((await client.set("payments", "region", "ap-south")).ok).toBe(true);

    const conflictOutcome = await client.recover(interrupted.recoveryHandle!);
    expect(conflictOutcome.ok).toBe(true);
    if (!conflictOutcome.ok) return;
    expect(conflictOutcome.value.status).toBe("Conflict");
    expect(conflictOutcome.value.recoveryHandle).toBeUndefined();
  });

  it("configurationRecovery_unguardedVault_reportsManualReconciliationWhenInterrupted (F-204 UC-004)", async () => {
    const unguardedItems = new Map<string, string>();
    const unguardedVault: IVaultProvider = {
      name: "bitwarden",
      recovery: undefined,
      async getSecret(secretName: string): Promise<Result<string>> {
        const val = unguardedItems.get(secretName);
        return val !== undefined ? ok(val) : fail("Not found", "NotFound");
      },
      async setSecret(secretName: string, secretValue: string): Promise<Result<void>> {
        unguardedItems.set(secretName, secretValue);
        return ok(undefined);
      },
      async deleteSecret(
        secretName: string
      ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
        unguardedItems.delete(secretName);
        return ok({ deleted: true, retained: false });
      }
    };

    const factory: IVaultProviderFactory = {
      kind: "bitwarden",
      create(): Result<IVaultProvider> {
        return ok(unguardedVault);
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "nyssa-app",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "bitwarden",
        kind: "bitwarden"
      },
      vaultFactories: [factory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect(
      (
        await client.set("payments", "vaultKey", "v1", {
          provider: "bitwarden"
        })
      ).ok
    ).toBe(true);

    client.setDocumentWriteInterceptor((ns) => fail(`Crash writing ${ns}`, "Unavailable"));
    const attempt = await client.mergeSettings("payments", { vaultKey: "v2" });

    expect(attempt.ok).toBe(false);
    if (attempt.ok) return;
    expect(attempt.progress).toBeDefined();
    expect(attempt.progress?.recovery).toBe("ManualReconciliation");
    expect(attempt.recoveryHandle).toBeUndefined();
  });

  it("configurationRecovery_tamperedAeadRecoveryToken_isRejectedDuringRecover (F-204 UC-005)", async () => {
    const vault = new GuardedVault();
    const factory: IVaultProviderFactory = {
      kind: "bitwarden",
      create(): Result<IVaultProvider> {
        return ok(vault);
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "nyssa-app",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "bitwarden",
        kind: "bitwarden"
      },
      vaultFactories: [factory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect((await client.set("cluster", "nodeAKey", "v1-a", { provider: "bitwarden" })).ok).toBe(true);
    expect((await client.set("cluster", "nodeBKey", "v1-b", { provider: "bitwarden" })).ok).toBe(true);

    vault.failOperationKey = "nodeBKey";
    const interrupted = await client.setNamespace("cluster", {
      nodeAKey: "v2-a",
      nodeBKey: "v2-b"
    });
    expect(interrupted.ok).toBe(false);
    if (interrupted.ok) return;
    expect(interrupted.recoveryHandle).toBeDefined();

    const rawToken = interrupted.recoveryHandle!.export();
    const tamperedChars = rawToken.slice(0, -2) + (rawToken.endsWith("AA") ? "BB" : "AA");
    const tamperedHandleRes = ConfigurationRecoveryHandle.import(tamperedChars);
    expect(tamperedHandleRes.ok).toBe(true);
    if (!tamperedHandleRes.ok) return;

    const tamperedRecover = await client.recover(tamperedHandleRes.value);
    expect(tamperedRecover.ok).toBe(false);
    if (!tamperedRecover.ok) {
      expect(tamperedRecover.category).toBe("Validation");
    }

    // Valid token recovers cleanly once vault is healthy
    vault.failOperationKey = undefined;
    const validRecover = await client.recover(interrupted.recoveryHandle!);
    expect(validRecover.ok).toBe(true);
    if (validRecover.ok) {
      expect(validRecover.value.status).toBe("Completed");
    }

    const nsRes = await client.getNamespace<{ nodeAKey: string; nodeBKey: string }>("cluster");
    expect(nsRes.ok && nsRes.value).toEqual({
      nodeAKey: "v2-a",
      nodeBKey: "v2-b"
    });
  });
});
