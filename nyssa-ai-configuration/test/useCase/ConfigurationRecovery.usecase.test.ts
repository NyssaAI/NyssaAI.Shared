import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { ConfigurationWriteRecoveryHandle } from "../../src/recovery/ConfigurationWriteRecoveryHandle.js";
import { PendingJournalManager } from "../../src/recovery/PendingJournalManager.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { ok, fail, type Result } from "../../src/results/Result.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "../../src/vault/IVaultProviderFactory.js";

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

  it("configurationRecovery_interruptedMultiKeyVaultWrite_returnsHandleAndRecoversOnRetryOrExplicitRecover (F-204 UC-001 & UC-002)", async () => {
    const vaultSecrets = new Map<string, string>();
    let failSecondVaultWrite = true;
    let vaultWriteAttempts = 0;

    const fakeVaultFactory: IVaultProviderFactory = {
      kind: "azure-key-vault",
      create(): Result<IVaultProvider> {
        return ok({
          name: "azureKeyVault",
          async getSecret(name: string): Promise<Result<string>> {
            const val = vaultSecrets.get(name);
            return val !== undefined ? ok(val) : fail("Not found", "NotFound");
          },
          async setSecret(name: string, val: string): Promise<Result<void>> {
            vaultWriteAttempts++;
            if (failSecondVaultWrite && vaultWriteAttempts === 2) {
              return fail("Transient vault timeout on second key.", "Unavailable");
            }
            vaultSecrets.set(name, val);
            return ok(undefined);
          },
          async deleteSecret(
            name: string
          ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
            vaultSecrets.delete(name);
            return ok({ deleted: true, retained: false });
          }
        });
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "RecoveryApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "azureKeyVault",
        kind: "azure-key-vault",
        settings: { vaultUri: "https://recovery.vault.azure.net/" },
        secrets: {}
      },
      vaultFactories: [fakeVaultFactory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Seed two external vault keys
    failSecondVaultWrite = false;
    expect(
      (
        await client.set("cluster", "nodeAKey", "v1-a", {
          encrypted: true,
          provider: "azureKeyVault"
        })
      ).ok
    ).toBe(true);
    expect(
      (
        await client.set("cluster", "nodeBKey", "v1-b", {
          encrypted: true,
          provider: "azureKeyVault"
        })
      ).ok
    ).toBe(true);

    // Attempt multi-key update where the 2nd vault write fails midway
    vaultWriteAttempts = 0;
    failSecondVaultWrite = true;
    const failedWrite = await client.setNamespace("cluster", {
      nodeAKey: "v2-a",
      nodeBKey: "v2-b"
    });
    expect(failedWrite.ok).toBe(false);
    if (failedWrite.ok) return;

    // Verify recoveryHandle is returned on the failed Result
    expect(failedWrite.recoveryHandle).toBeDefined();
    const handle = failedWrite.recoveryHandle!;

    // Export and import recovery handle across process boundary (F-204 UC-002)
    const exportedToken = handle.export();
    const importedRes = ConfigurationWriteRecoveryHandle.import(exportedToken);
    expect(importedRes.ok).toBe(true);
    if (!importedRes.ok) return;

    // Tampered journalPath outside .pending/ is rejected
    const tamperedToken = Buffer.from(
      JSON.stringify({
        version: 1,
        namespace: "cluster",
        operationId: handle.operationId,
        journalPath: path.join(configDir, "cluster.json")
      }),
      "utf8"
    ).toString("base64url");
    const tamperedRes = ConfigurationWriteRecoveryHandle.import(tamperedToken);
    expect(tamperedRes.ok).toBe(false);

    // Recover using imported handle once vault is healthy again
    failSecondVaultWrite = false;
    const recoverRes = await client.recover(importedRes.value);
    expect(recoverRes.ok).toBe(true);

    // Verify both vault secrets and local document are converged and journal is deleted
    expect(vaultSecrets.get("cluster/nodeAKey")).toBe("v2-a");
    expect(vaultSecrets.get("cluster/nodeBKey")).toBe("v2-b");
    const journalManager = new PendingJournalManager(configDir);
    expect(journalManager.hasWriteJournal("cluster")).toBe(false);

    const readNs = await client.getNamespace<{ nodeAKey: string; nodeBKey: string }>("cluster");
    expect(readNs.ok && readNs.value).toEqual({
      nodeAKey: "v2-a",
      nodeBKey: "v2-b"
    });
  });

  it("configurationRecovery_pendingMoveCleanupJournal_completesSourceVaultDeleteOnNextOperation (F-204 UC-003)", async () => {
    const vaultSecrets = new Map<string, string>([["legacy/apiToken", "secret-in-vault"]]);

    const fakeVaultFactory: IVaultProviderFactory = {
      kind: "azure-key-vault",
      create(): Result<IVaultProvider> {
        return ok({
          name: "azureKeyVault",
          async getSecret(name: string): Promise<Result<string>> {
            const val = vaultSecrets.get(name);
            return val !== undefined ? ok(val) : fail("Not found", "NotFound");
          },
          async setSecret(name: string, val: string): Promise<Result<void>> {
            vaultSecrets.set(name, val);
            return ok(undefined);
          },
          async deleteSecret(
            name: string
          ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
            vaultSecrets.delete(name);
            return ok({ deleted: true, retained: false });
          }
        });
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "MoveRecoveryApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "azureKeyVault",
        kind: "azure-key-vault",
        settings: { vaultUri: "https://move.vault.azure.net/" },
        secrets: {}
      },
      vaultFactories: [fakeVaultFactory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Seed local key after destination write succeeded, and simulate a leftover .pending/legacy.move.json
    expect((await client.set("legacy", "apiToken", "secret-in-vault")).ok).toBe(true);
    const journalManager = new PendingJournalManager(configDir);
    expect(
      journalManager.createMoveJournal("legacy", "apiToken", "azureKeyVault", "legacy/apiToken").ok
    ).toBe(true);
    expect(journalManager.hasMoveJournal("legacy")).toBe(true);

    // Next write/remove/move on "legacy" replays the move cleanup journal and deletes the vault secret
    expect((await client.set("legacy", "otherKey", "ok")).ok).toBe(true);
    expect(vaultSecrets.has("legacy/apiToken")).toBe(false);
    expect(journalManager.hasMoveJournal("legacy")).toBe(false);
  });

  it("configurationRecovery_concurrentEditConflictVsManualReconciliation_detectsSha256Correctly (F-204 UC-004)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "ConflictRecoveryApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect((await client.set("orders", "endpoint", "https://v1.orders.local")).ok).toBe(true);

    const docPath = path.join(configDir, "orders.json");
    const journalManager = new PendingJournalManager(configDir);
    const originalSha = PendingJournalManager.computeDocumentSha256(docPath);

    // Create a pending write journal expecting originalSha and planning v2
    const plannedDoc = {
      endpoint: { value: "https://v2.orders.local" }
    };
    const handleRes = journalManager.createWriteJournal("orders", originalSha, plannedDoc, []);
    expect(handleRes.ok).toBe(true);
    if (!handleRes.ok) return;

    // Case A: Another writer modifies orders.json to an unrelated state (v3)
    const concurrentDoc = {
      endpoint: { value: "https://v3-concurrent.orders.local" }
    };
    expect(ProtectedFileSystem.createFile(docPath, JSON.stringify(concurrentDoc, null, 2)).ok).toBe(
      true
    );

    // Recovery detects SHA-256 mismatch against both expectedExistingSha256 and plannedNewSha256 -> Conflict
    const conflictRes = await client.recover(handleRes.value);
    expect(conflictRes.ok).toBe(false);
    if (!conflictRes.ok) {
      expect(conflictRes.category).toBe("Conflict");
    }
    // Journal is preserved so operator can inspect
    expect(journalManager.hasWriteJournal("orders")).toBe(true);

    // Case B: Operator manually reconciles orders.json to match plannedDocument (v2)
    expect(ProtectedFileSystem.createFile(docPath, JSON.stringify(plannedDoc, null, 2)).ok).toBe(
      true
    );
    const reconciledRes = await client.recover(handleRes.value);
    expect(reconciledRes.ok).toBe(true);
    expect(journalManager.hasWriteJournal("orders")).toBe(false);
  });
});
