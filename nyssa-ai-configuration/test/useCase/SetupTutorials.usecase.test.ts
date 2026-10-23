import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { ok, fail, type Result } from "../../src/results/Result.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "../../src/vault/IVaultProviderFactory.js";
import type { VaultOptions } from "../../src/manifest/VaultOptions.js";

describe("SetupTutorials Public Use Cases (F-103)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-setup-tutorials-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
    configDir = path.join(tempDir, "config");
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("setupTutorials_firstTimeInitialize_bootstrapsManifestAndProtectedDirectory (F-103 UC-001)", async () => {
    const keyring = new InMemoryKeyring();

    const initRes = await ConfigurationClient.initialize({
      applicationName: "FirstRunCLI",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(initRes.ok).toBe(true);
    if (!initRes.ok) return;

    expect(fs.existsSync(manifestPath)).toBe(true);
    expect(fs.existsSync(configDir)).toBe(true);

    const manifestJson = JSON.parse(fs.readFileSync(manifestPath, "utf8")) as {
      version: number;
      applications: Record<string, { configurationDirectory: string; lockTimeout: string }>;
    };
    expect(manifestJson.version).toBe(1);
    expect(manifestJson.applications["FirstRunCLI"]?.configurationDirectory).toBe(
      path.resolve(configDir)
    );
  });

  it("setupTutorials_manifestKeyVaultOverridesCodeKeyVaultWhole_usesManifestVaultConfiguration (F-103 UC-002)", async () => {
    const keyring = new InMemoryKeyring();
    expect(ProtectedFileSystem.createDirectory(configDir).ok).toBe(true);

    // Pre-populate manifest with a whole keyVault object
    const manifestContent = {
      version: 1,
      applications: {
        EnterpriseService: {
          configurationDirectory: configDir,
          lockTimeout: "00:00:20",
          keyVault: {
            name: "corpVault",
            kind: "azure-key-vault",
            settings: {
              vaultUri: "https://manifest-vault.vault.azure.net/"
            },
            secrets: {
              clientSecret: "manifest-secret"
            }
          }
        }
      }
    };
    expect(
      ProtectedFileSystem.createFile(manifestPath, JSON.stringify(manifestContent, null, 2)).ok
    ).toBe(true);

    let capturedVaultOptions: VaultOptions | undefined;
    const fakeVaultProvider: IVaultProvider = {
      name: "corpVault",
      async getSecret(): Promise<Result<string>> {
        return ok("secret-from-manifest-vault");
      },
      async setSecret(): Promise<Result<void>> {
        return ok(undefined);
      },
      async deleteSecret(): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
        return ok({ deleted: true, retained: false });
      }
    };

    const fakeFactory: IVaultProviderFactory = {
      kind: "azure-key-vault",
      create(options: VaultOptions): Result<IVaultProvider> {
        capturedVaultOptions = options;
        if (!options.name) {
          return fail("Missing name");
        }
        return ok(fakeVaultProvider);
      }
    };

    // Pass different keyVault in code options; manifest keyVault must win as a whole object
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "EnterpriseService",
      configManifest: manifestPath,
      keyring,
      keyVault: {
        name: "corpVault",
        kind: "azure-key-vault",
        settings: {
          vaultUri: "https://code-vault.vault.azure.net/",
          extraCodeSetting: "should-not-merge"
        },
        secrets: {
          clientSecret: "code-secret"
        }
      },
      vaultFactories: [fakeFactory]
    });

    expect(clientRes.ok).toBe(true);
    expect(capturedVaultOptions).toBeDefined();
    expect(capturedVaultOptions?.settings?.["vaultUri"]).toBe(
      "https://manifest-vault.vault.azure.net/"
    );
    expect(capturedVaultOptions?.settings?.["extraCodeSetting"]).toBeUndefined();
    expect(capturedVaultOptions?.secrets?.["clientSecret"]).toBe("manifest-secret");
  });

  it("setupTutorials_isolatedTestDoubles_neverTouchesUserHomeDirectory (F-103 UC-003)", async () => {
    const keyring = new InMemoryKeyring();
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "IsolatedTestApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;

    const setRes = await clientRes.value.set("isolated", "secretKey", "in-memory-encrypted", {
      encrypted: true,
      provider: "aesLocal"
    });
    expect(setRes.ok).toBe(true);

    // Master key was stored in the injected InMemoryKeyring
    const mkRes = await keyring.get("nyssa.configuration", "master-key.v1");
    expect(mkRes.ok).toBe(true);
  });
});
