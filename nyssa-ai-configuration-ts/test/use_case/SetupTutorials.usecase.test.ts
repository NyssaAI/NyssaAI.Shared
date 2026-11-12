import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { ConfigurationManifestWriter } from "../../src/manifest/ConfigurationManifestWriter.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { KeyringDefault } from "../../src/keyring/KeyringDefault.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { ok, fail, type Result } from "@nyssa-ai/result";
import type { IKeyring } from "../../src/keyring/IKeyring.js";
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
      keyVaults?: unknown;
      applications: Record<string, { configurationDirectory: string; lockTimeoutMs: number }>;
    };
    expect(manifestJson.version).toBe(1);
    expect(manifestJson.keyVaults).toBeUndefined();
    expect(manifestJson.applications["FirstRunCLI"]?.configurationDirectory).toBe(
      path.resolve(configDir)
    );
    expect(manifestJson.applications["FirstRunCLI"]?.lockTimeoutMs).toBe(10000);
  });

  it("setupTutorials_firstTimeInitializeWithoutConfigDirectory_defaultsToManifestSiblingAppDir", async () => {
    const keyring = new InMemoryKeyring();

    const initRes = await ConfigurationClient.initialize({
      applicationName: "DefaultPathCLI",
      configManifest: manifestPath,
      keyring
    });
    expect(initRes.ok).toBe(true);
    if (!initRes.ok) return;

    const expectedDefaultDir = path.join(path.dirname(manifestPath), "DefaultPathCLI");
    expect(fs.existsSync(expectedDefaultDir)).toBe(true);

    const manifestJson = JSON.parse(fs.readFileSync(manifestPath, "utf8")) as {
      version: number;
      applications: Record<string, { configurationDirectory: string; lockTimeoutMs: number }>;
    };
    expect(manifestJson.applications["DefaultPathCLI"]?.configurationDirectory).toBe(
      expectedDefaultDir
    );
    expect(manifestJson.applications["DefaultPathCLI"]?.lockTimeoutMs).toBe(10000);
  });

  it("setupTutorials_manifestKeyVaultOverridesSameNamedCodeVaultWholeAndCoexistsWithOtherCodeVaults (F-103 UC-002)", async () => {
    const keyring = new InMemoryKeyring();
    expect(ProtectedFileSystem.createDirectory(configDir).ok).toBe(true);

    expect(
      ConfigurationManifestWriter.setConfigurationDirectory(
        manifestPath,
        "EnterpriseService",
        configDir,
        20000
      ).ok
    ).toBe(true);

    expect(
      (
        await ConfigurationManifestWriter.setVault(
          manifestPath,
          {
            name: "corpVault",
            kind: "azure-key-vault",
            settings: {
              vaultUri: "https://manifest-vault.vault.azure.net/"
            },
            secrets: {
              clientSecret: "manifest-secret"
            }
          },
          { keyring }
        )
      ).ok
    ).toBe(true);

    const capturedVaults = new Map<string, VaultOptions>();
    const fakeFactory: IVaultProviderFactory = {
      kind: "azure-key-vault",
      create(options: VaultOptions): Result<IVaultProvider> {
        capturedVaults.set(options.name, options);
        if (!options.name) {
          return fail("Missing name");
        }
        return ok({
          name: options.name,
          async getSecret(): Promise<Result<string>> {
            return ok(`secret-from-${options.name}`);
          },
          async setSecret(): Promise<Result<void>> {
            return ok(undefined);
          },
          async deleteSecret(): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
            return ok({ deleted: true, retained: false });
          }
        });
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "EnterpriseService",
      configManifest: manifestPath,
      keyring,
      keyVault: {
        name: "corpVault",
        kind: "azure-key-vault",
        cacheTTLSeconds: 180,
        settings: {
          vaultUri: "https://code-vault.vault.azure.net/",
          extraCodeSetting: "should-not-merge"
        },
        secrets: {
          clientSecret: "code-secret"
        }
      },
      keyVaults: [
        {
          name: "devVault",
          kind: "azure-key-vault",
          settings: {
            vaultUri: "https://dev-vault.vault.azure.net/"
          }
        }
      ],
      vaultFactories: [fakeFactory]
    });

    expect(clientRes.ok).toBe(true);
    const corpOpts = capturedVaults.get("corpVault");
    expect(corpOpts).toBeDefined();
    expect(corpOpts?.settings?.["vaultUri"]).toBe("https://manifest-vault.vault.azure.net/");
    expect(corpOpts?.settings?.["extraCodeSetting"]).toBeUndefined();
    expect(corpOpts?.secrets?.["clientSecret"]).toBe("manifest-secret");
    expect(corpOpts?.cacheTTLSeconds).toBe(180);

    const devOpts = capturedVaults.get("devVault");
    expect(devOpts?.settings?.["vaultUri"]).toBe("https://dev-vault.vault.azure.net/");
  });

  it("setupTutorials_aadTransplantAcrossVaults_failsValidationAtBootstrap", async () => {
    const keyring = new InMemoryKeyring();

    const setRes = await ConfigurationManifestWriter.setVault(
      manifestPath,
      {
        name: "bws-staging",
        kind: "bitwarden-secrets",
        settings: { projectId: "proj-staging" },
        secrets: { accessToken: "staging-token-secret" }
      },
      { keyring }
    );
    expect(setRes.ok).toBe(true);
    if (!setRes.ok) return;

    // Transplant encrypted envelope from bws-staging onto bws-prod on disk
    const parsed = JSON.parse(fs.readFileSync(manifestPath, "utf8")) as {
      version: number;
      keyVaults: Record<string, unknown>;
      applications: Record<string, unknown>;
    };
    parsed.keyVaults["bws-prod"] = parsed.keyVaults["bws-staging"];
    delete parsed.keyVaults["bws-staging"];
    ProtectedFileSystem.createFile(manifestPath, JSON.stringify(parsed, null, 2));

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "TransplantVictimApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });

    expect(clientRes.ok).toBe(false);
    if (!clientRes.ok) {
      expect(clientRes.category).toBe("Validation");
      expect(clientRes.message).toContain("keyVaults.bws-prod.secrets.accessToken");
      expect(clientRes.message).not.toContain("staging-token-secret");
    }
  });

  it("setupTutorials_missingManifestMasterKeyOrSecretlessVault_enforcesReadOnlyKeyringContract", async () => {
    // 1. Secretless manifest vault never queries keyring for '_manifest'
    expect(
      (
        await ConfigurationManifestWriter.setVault(manifestPath, {
          name: "oauthVault",
          kind: "bitwarden-secrets",
          credential: "auth/bwsGrant",
          settings: { projectId: "proj-oauth" }
        })
      ).ok
    ).toBe(true);

    let manifestKeyringGets = 0;
    const spyManifestKeyring: IKeyring = {
      async get() {
        manifestKeyringGets++;
        return fail("should not be called", "Unavailable");
      },
      async set() {
        return fail("should not be called", "Forbidden");
      },
      async delete() {
        return fail("should not be called", "Forbidden");
      }
    };

    const secretlessInit = await ConfigurationClient.initialize({
      applicationName: "SecretlessVaultApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring: new InMemoryKeyring(),
      manifestKeyring: spyManifestKeyring
    });
    expect(secretlessInit.ok).toBe(true);
    expect(manifestKeyringGets).toBe(0);

    // 2. Vault with encrypted secret + empty manifestKeyring fails closed without creating a key
    const writerKeyring = new InMemoryKeyring();
    expect(
      (
        await ConfigurationManifestWriter.setVault(
          manifestPath,
          {
            name: "bws-prod",
            kind: "bitwarden-secrets",
            settings: { projectId: "proj-prod" },
            secrets: { accessToken: "prod-secret" }
          },
          { keyring: writerKeyring }
        )
      ).ok
    ).toBe(true);

    const emptyManifestKeyring = new InMemoryKeyring();
    const missingKeyInit = await ConfigurationClient.initialize({
      applicationName: "MissingManifestKeyApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring: new InMemoryKeyring(),
      manifestKeyring: emptyManifestKeyring
    });
    expect(missingKeyInit.ok).toBe(false);
    if (!missingKeyInit.ok) {
      expect(missingKeyInit.category).toBe("Validation");
      expect(missingKeyInit.message).toBe(
        "configuration: manifest encryption key 'nyssa-ai-configuration/_manifest' was not found in the keyring."
      );
    }
    expect(emptyManifestKeyring.writes.length).toBe(0);

    // 3. Passing writerKeyring as manifestKeyring allows a brand-new app to inherit and unseal root keyVaults!
    const newAppInit = await ConfigurationClient.initialize({
      applicationName: "BrandNewAppInSharedManifest",
      configManifest: manifestPath,
      keyring: new InMemoryKeyring(),
      manifestKeyring: writerKeyring
    });
    expect(newAppInit.ok).toBe(true);
    const mk = await writerKeyring.get(KeyringDefault.SERVICE_NAME, KeyringDefault.MANIFEST_ACCOUNT);
    expect(mk.ok && mk.value).not.toBeNull();
  });

  it("setupTutorials_duplicateProgrammaticVaultNames_failsValidation", async () => {
    const dupRes = await ConfigurationClient.initialize({
      applicationName: "DupVaultApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring: new InMemoryKeyring(),
      keyVault: { name: "bws-prod", kind: "bitwarden-secrets", secrets: { accessToken: "t1" } },
      keyVaults: [{ name: "bws-prod", kind: "bitwarden-secrets", secrets: { accessToken: "t2" } }]
    });
    expect(dupRes.ok).toBe(false);
    if (!dupRes.ok) {
      expect(dupRes.category).toBe("Validation");
    }
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

    const mkRes = await keyring.get("nyssa-ai-configuration", "IsolatedTestApp");
    expect(mkRes.ok).toBe(true);
    if (mkRes.ok) {
      expect(mkRes.value).not.toBeNull();
    }
  });
});
