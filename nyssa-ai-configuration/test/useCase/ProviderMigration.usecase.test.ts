import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { ok, fail, type Result } from "../../src/results/Result.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "../../src/vault/IVaultProviderFactory.js";

describe("ProviderMigration Public Use Cases (F-201)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  let keyring: InMemoryKeyring;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-provider-migration-"));
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

  it("providerMigration_movePlaintextToAesLocalAndBack_updatesProviderWithoutChangingLogicalValue (F-201 UC-001 & UC-002)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "MigrationApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Seed plaintext value
    expect((await client.set("db", "connectionString", "Server=prod;Password=123;")).ok).toBe(true);

    // Move Default -> aesLocal
    const toEncryptedRes = await client.move("db", "connectionString", "aesLocal");
    expect(toEncryptedRes.ok).toBe(true);
    if (toEncryptedRes.ok) {
      expect(toEncryptedRes.value).toEqual({
        sourceValueRemains: false
      });
    }

    // Verify encrypted on disk
    const rawAfterEncrypt = fs.readFileSync(path.join(configDir, "db.json"), "utf8");
    expect(rawAfterEncrypt).not.toContain("Server=prod;Password=123;");

    // Logical value remains identical
    const readEncrypted = await client.get("db", "connectionString");
    expect(readEncrypted.ok && readEncrypted.value).toBe("Server=prod;Password=123;");

    // Move aesLocal -> Default
    const toPlainRes = await client.move("db", "connectionString", "Default");
    expect(toPlainRes.ok).toBe(true);
    if (toPlainRes.ok) {
      expect(toPlainRes.value).toEqual({
        sourceValueRemains: false
      });
    }

    const rawAfterPlain = fs.readFileSync(path.join(configDir, "db.json"), "utf8");
    expect(rawAfterPlain).toContain("Server=prod;Password=123;");
  });

  it("providerMigration_moveFromExternalVaultWhenSourceDeleteFails_commitsLocalAndReportsSourceValueRemains (F-201 UC-003)", async () => {
    const vaultStorage = new Map<string, string>();
    let failDelete = false;

    const fakeVault: IVaultProvider = {
      name: "azureKeyVault",
      async getSecret(secretName: string): Promise<Result<string>> {
        const val = vaultStorage.get(secretName);
        if (val === undefined) return fail(`Secret '${secretName}' not found.`, "NotFound");
        return ok(val);
      },
      async setSecret(secretName: string, value: string): Promise<Result<void>> {
        vaultStorage.set(secretName, value);
        return ok(undefined);
      },
      async deleteSecret(
        secretName: string
      ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
        if (failDelete) {
          return fail("Vault delete permission denied by soft-delete policy.", "Forbidden");
        }
        vaultStorage.delete(secretName);
        return ok({ deleted: true, retained: false });
      }
    };

    const fakeFactory: IVaultProviderFactory = {
      kind: "azure-key-vault",
      create(): Result<IVaultProvider> {
        return ok(fakeVault);
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "VaultMigrationApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "azureKeyVault",
        kind: "azure-key-vault",
        settings: { vaultUri: "https://test.vault.azure.net/" },
        secrets: {}
      },
      vaultFactories: [fakeFactory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Seed secret in external vault
    expect(
      (
        await client.set("payments", "webhookSecret", "whsec_live_999", {
          encrypted: true,
          provider: "azureKeyVault"
        })
      ).ok
    ).toBe(true);
    expect(vaultStorage.get("payments/webhookSecret")).toBe("whsec_live_999");

    // Simulate source delete failure during move to aesLocal
    failDelete = true;
    const moveRes = await client.move("payments", "webhookSecret", "aesLocal");
    expect(moveRes.ok).toBe(true);
    if (moveRes.ok) {
      expect(moveRes.value.sourceValueRemains).toBe(true);
      expect(moveRes.value.sourceDeleteError).toContain("soft-delete policy");
    }

    // Local document now uses aesLocal and resolves the value cleanly
    const getRes = await client.get("payments", "webhookSecret");
    expect(getRes.ok && getRes.value).toBe("whsec_live_999");
  });
});
