import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { ok, fail, type Result } from "../../src/results/Result.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "../../src/vault/IVaultProviderFactory.js";

describe("VaultProvider Public Use Cases (F-203)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  let keyring: InMemoryKeyring;
  let vaultStorage: Map<string, string>;
  let fakeFactory: IVaultProviderFactory;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-vault-provider-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
    configDir = path.join(tempDir, "config");
    keyring = new InMemoryKeyring();
    vaultStorage = new Map<string, string>();

    const fakeVault: IVaultProvider = {
      name: "azureKeyVault",
      async getSecret(secretName: string): Promise<Result<string>> {
        const val = vaultStorage.get(secretName);
        if (val === undefined) return fail(`Vault secret '${secretName}' not found.`, "NotFound");
        return ok(val);
      },
      async setSecret(secretName: string, value: string): Promise<Result<void>> {
        vaultStorage.set(secretName, value);
        return ok(undefined);
      },
      async deleteSecret(
        secretName: string
      ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
        vaultStorage.delete(secretName);
        return ok({ deleted: true, retained: false });
      }
    };

    fakeFactory = {
      kind: "azure-key-vault",
      create(): Result<IVaultProvider> {
        return ok(fakeVault);
      }
    };
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("vaultProvider_storesVersionlessPointerAndAssemblesMultiProviderNamespace (F-203 UC-001 & UC-002)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "VaultApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "azureKeyVault",
        kind: "azure-key-vault",
        settings: { vaultUri: "https://my-vault.vault.azure.net/" },
        secrets: {}
      },
      vaultFactories: [fakeFactory]
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // 1. Plaintext Default key
    expect((await client.set("billing", "currency", "USD")).ok).toBe(true);
    // 2. Local AES-256-GCM key
    expect(
      (
        await client.set("billing", "webhookSigningKey", "whsec_local_123", {
          encrypted: true,
          provider: "aesLocal"
        })
      ).ok
    ).toBe(true);
    // 3. External vault key
    expect(
      (
        await client.set("billing", "stripeSecretKey", "sk_live_vault_999", {
          encrypted: true,
          provider: "azureKeyVault"
        })
      ).ok
    ).toBe(true);

    // Inspect local JSON: stripeSecretKey stores only the versionless pointer "billing/stripeSecretKey"
    const rawDoc = JSON.parse(fs.readFileSync(path.join(configDir, "billing.json"), "utf8")) as Record<
      string,
      { value: unknown; encrypted?: boolean; provider?: string }
    >;
    expect(rawDoc["stripeSecretKey"]).toEqual({
      value: "billing/stripeSecretKey",
      encrypted: true,
      provider: "azureKeyVault"
    });
    expect(vaultStorage.get("billing/stripeSecretKey")).toBe("sk_live_vault_999");

    // Multi-provider namespace assembly via getNamespace
    const assembled = await client.getNamespace<{
      currency: string;
      webhookSigningKey: string;
      stripeSecretKey: string;
    }>("billing");
    expect(assembled.ok).toBe(true);
    if (assembled.ok) {
      expect(assembled.value).toEqual({
        currency: "USD",
        webhookSigningKey: "whsec_local_123",
        stripeSecretKey: "sk_live_vault_999"
      });
    }

    // Multi-provider namespace update via setNamespace preserves each key's provider
    const updateNs = await client.setNamespace("billing", {
      currency: "EUR",
      webhookSigningKey: "whsec_local_456",
      stripeSecretKey: "sk_live_vault_rotated"
    });
    expect(updateNs.ok).toBe(true);
    expect(vaultStorage.get("billing/stripeSecretKey")).toBe("sk_live_vault_rotated");
  });

  it("vaultProvider_circularVaultCredentialReference_failsWithConflictWithoutRecursion (F-203 UC-003)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "CircularVaultApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      keyVault: {
        name: "azureKeyVault",
        kind: "azure-key-vault",
        credential: "vault/bootstrapSecret",
        settings: { vaultUri: "https://my-vault.vault.azure.net/" }
      },
      vaultFactories: [fakeFactory]
    });

    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Attempting to store "vault/bootstrapSecret" inside "azureKeyVault" (which uses "vault/bootstrapSecret" to authenticate) is rejected
    const circularWrite = await client.set("vault", "bootstrapSecret", "secret-val", {
      encrypted: true,
      provider: "azureKeyVault"
    });
    expect(circularWrite.ok).toBe(false);
    if (!circularWrite.ok) {
      expect(circularWrite.category).toBe("Conflict");
      expect(circularWrite.error).toContain("circular credential");
    }
  });
});
