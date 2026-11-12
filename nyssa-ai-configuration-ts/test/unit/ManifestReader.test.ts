import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import { ConfigurationManifestReader } from "../../src/manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "../../src/manifest/ConfigurationManifestWriter.js";
import { TimeSpanParser } from "../../src/manifest/TimeSpanParser.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";

describe("ConfigurationManifestReader and Writer", () => {
  let tempDir: string;
  let manifestPath: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-manifest-test-"));
    ProtectedFileSystem.createDirectory(tempDir);
    manifestPath = path.join(tempDir, "config-manifest.json");
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("timeSpanParser_dotNetFormat_parsesToMilliseconds", () => {
    const res1 = TimeSpanParser.parse("00:00:10");
    expect(res1.ok).toBe(true);
    if (res1.ok) expect(res1.value).toBe(10000);

    const res2 = TimeSpanParser.parse("00:01:30.500");
    expect(res2.ok).toBe(true);
    if (res2.ok) expect(res2.value).toBe(90500);
  });

  it("manifestWriter_newApplication_provisionsMinimalManifestOmittingEmptyKeyVaults", () => {
    const configDir = path.join(tempDir, "app-config");
    const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
      manifestPath,
      "Cool-CLI",
      configDir,
      5000
    );

    expect(writeRes.ok).toBe(true);
    expect(fs.existsSync(manifestPath)).toBe(true);

    const rawOnDisk = JSON.parse(fs.readFileSync(manifestPath, "utf8")) as Record<string, unknown>;
    expect(Object.prototype.hasOwnProperty.call(rawOnDisk, "keyVaults")).toBe(false);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value.configurationDirectory).toBe(path.resolve(configDir));
      expect(readRes.value.lockTimeoutMs).toBe(5000);
      expect(readRes.value.keyVaults).toEqual({});
    }
  });

  it("manifestReader_relativeDir_resolvesRelativeToManifest", () => {
    const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
      manifestPath,
      "RelativeApp",
      "./relative-config"
    );
    expect(writeRes.ok).toBe(true);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "RelativeApp");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      const expected = path.resolve(tempDir, "relative-config");
      expect(readRes.value.configurationDirectory).toBe(expected);
    }
  });

  it("manifestReader_withCommentsAndTrailingCommas_parsesRootKeyVaultsAndTimeout", () => {
    const annotatedJsonc = `{
      // Manifest version
      "version": 1,
      /* Shared root vault configuration */
      "keyVaults": {
        "corpVault": {
          "kind": "azure-key-vault",
          "endpoint": "https://corp.vault.azure.net/",
          "credential": "oauth://vault/token",
          "cacheTTLSeconds": 90,
          "settings": {
            "vaultUri": "https://corp.vault.azure.net/",
          },
        },
      },
      "applications": {
        "Cool-CLI": {
          "configurationDirectory": "Cool-CLI",
          "lockTimeout": "00:00:02",
        },
      },
    }`;
    ProtectedFileSystem.createFile(manifestPath, annotatedJsonc);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value.configurationDirectory).toBe(path.resolve(tempDir, "Cool-CLI"));
      expect(readRes.value.lockTimeoutMs).toBe(2000);
      const vault = readRes.value.keyVaults?.["corpVault"];
      expect(vault?.name).toBe("corpVault");
      expect(vault?.endpoint).toBe("https://corp.vault.azure.net/");
      expect(vault?.credential).toBe("oauth://vault/token");
      expect(vault?.cacheTTLSeconds).toBe(90);
      expect(vault?.settings).toEqual({
        vaultUri: "https://corp.vault.azure.net/"
      });
      expect(vault?.secrets).toEqual({});
    }
  });

  it("manifestReader_legacyPerAppKeyVault_failsValidationClosed", () => {
    ProtectedFileSystem.createFile(
      manifestPath,
      JSON.stringify({
        version: 1,
        applications: {
          "Cool-CLI": {
            configurationDirectory: "Cool-CLI",
            keyVault: {
              name: "corp",
              kind: "azure-key-vault"
            }
          }
        }
      })
    );

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(false);
    if (!readRes.ok) {
      expect(readRes.category).toBe("Validation");
      expect(readRes.message).toBe(
        "manifest: applications.Cool-CLI.keyVault is no longer supported; move vault definitions to root 'keyVaults'"
      );
    }
  });

  it("manifestReader_unsupportedVersionOrNonPositiveTimeout_failsValidation", () => {
    ProtectedFileSystem.createFile(
      manifestPath,
      JSON.stringify({ version: 2, applications: { App: { configurationDirectory: "App" } } })
    );
    const badVersion = ConfigurationManifestReader.readApplication(manifestPath, "App");
    expect(badVersion.ok).toBe(false);
    if (!badVersion.ok) expect(badVersion.category).toBe("Validation");

    ProtectedFileSystem.createFile(
      manifestPath,
      JSON.stringify({
        version: 1,
        applications: { App: { configurationDirectory: "App", lockTimeout: "00:00:00" } }
      })
    );
    const zeroTimeout = ConfigurationManifestReader.readApplication(manifestPath, "App");
    expect(zeroTimeout.ok).toBe(false);
    if (!zeroTimeout.ok) expect(zeroTimeout.category).toBe("Validation");
  });

  it("manifestReader_readable0644FileWithRootOrLegacySecrets_failsForbiddenFirstWithoutLeakingSecret", () => {
    const makeWorldReadable = (target: string) => {
      if (process.platform === "win32") {
        execFileSync("icacls.exe", [target, "/grant", "*S-1-1-0:(R)"], { stdio: "ignore" });
      } else {
        fs.chmodSync(target, 0o644);
      }
    };

    // Case 1: Root keyVaults with plaintext/malformed secret in 0644 file -> Forbidden FIRST (not Validation!)
    ProtectedFileSystem.createFile(
      manifestPath,
      JSON.stringify({
        version: 1,
        keyVaults: {
          "bws-prod": {
            kind: "bitwarden-secrets",
            secrets: {
              accessToken: "super-sensitive-plaintext-token"
            }
          }
        },
        applications: {
          "Cool-CLI": { configurationDirectory: "Cool-CLI" }
        }
      })
    );
    makeWorldReadable(manifestPath);

    const rootRes = ConfigurationManifestReader.readManifest(manifestPath);
    expect(rootRes.ok).toBe(false);
    if (!rootRes.ok) {
      expect(rootRes.category).toBe("Forbidden");
      expect(rootRes.message).toContain("is readable by");
      if (process.platform !== "win32") {
        expect(rootRes.message).toContain("chmod 600");
      }
      expect(rootRes.message).not.toContain("super-sensitive-plaintext-token");
    }

    // Case 2: Legacy applications.<app>.keyVault.secrets in 0644 file -> Forbidden FIRST (before legacy Validation!)
    ProtectedFileSystem.createFile(
      manifestPath,
      JSON.stringify({
        version: 1,
        applications: {
          "Cool-CLI": {
            configurationDirectory: "Cool-CLI",
            keyVault: {
              name: "corp",
              kind: "azure-key-vault",
              secrets: {
                clientSecret: "legacy-fictional-secret"
              }
            }
          }
        }
      })
    );
    makeWorldReadable(manifestPath);

    const legacyRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(legacyRes.ok).toBe(false);
    if (!legacyRes.ok) {
      expect(legacyRes.category).toBe("Forbidden");
      expect(legacyRes.message).toContain("is readable by");
      expect(legacyRes.message).not.toContain("legacy-fictional-secret");
    }
  });

  it.each([
    ["raw plaintext string", "leaked-raw-secret-999"],
    ["encrypted false object", { value: "leaked-raw-secret-999", encrypted: false, provider: "aesLocal" }],
    ["wrong provider", { value: "leaked-raw-secret-999", encrypted: true, provider: "other" }],
    ["short non-v2 base64url", { value: "YWJjZGVm", encrypted: true, provider: "aesLocal" }],
    [
      "wrong format version byte 0x01",
      {
        value: Buffer.concat([Buffer.from([0x01]), crypto.randomBytes(32)])
          .toString("base64")
          .replace(/=/g, "")
          .replace(/\+/g, "-")
          .replace(/\//g, "_"),
        encrypted: true,
        provider: "aesLocal"
      }
    ]
  ])(
    "manifestReader_invalidSecretEnvelope_%s_failsValidationWithoutLeaking",
    (_label, badSecretValue) => {
      ProtectedFileSystem.createFile(
        manifestPath,
        JSON.stringify({
          version: 1,
          keyVaults: {
            "bws-prod": {
              kind: "bitwarden-secrets",
              secrets: {
                accessToken: badSecretValue
              }
            }
          },
          applications: {}
        })
      );

      const res = ConfigurationManifestReader.readManifest(manifestPath);
      expect(res.ok).toBe(false);
      if (!res.ok) {
        expect(res.category).toBe("Validation");
        expect(res.message).toBe(
          "manifest: keyVaults.bws-prod.secrets.accessToken must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json"
        );
        expect(res.message).not.toContain("leaked-raw-secret-999");
      }
    }
  );

  it("manifestReader_invalidVaultNamesFieldsAndMismatchedEntryName_failValidation", () => {
    const invalidVaultManifests = [
      { keyVaults: { aesLocal: { kind: "bitwarden-secrets" } } },
      { keyVaults: { literal: { kind: "bitwarden-secrets" } } },
      { keyVaults: { oauth: { kind: "bitwarden-secrets" } } },
      { keyVaults: { "bad/name": { kind: "bitwarden-secrets" } } },
      { keyVaults: { "bad name": { kind: "bitwarden-secrets" } } },
      { keyVaults: { corp: { name: "mismatch", kind: "bitwarden-secrets" } } },
      { keyVaults: { corp: { kind: "   " } } },
      { keyVaults: { corp: { kind: "bitwarden-secrets", endpoint: "not-a-url" } } },
      { keyVaults: { corp: { kind: "bitwarden-secrets", credential: "invalid-single-segment" } } },
      { keyVaults: { corp: { kind: "bitwarden-secrets", cacheTTLSeconds: -5 } } },
      { keyVaults: { corp: { kind: "bitwarden-secrets", settings: { cacheTTLSeconds: "abc" } } } }
    ];

    for (const partial of invalidVaultManifests) {
      ProtectedFileSystem.createFile(
        manifestPath,
        JSON.stringify({ version: 1, ...partial, applications: {} })
      );
      const res = ConfigurationManifestReader.readManifest(manifestPath);
      expect(res.ok).toBe(false);
      if (!res.ok) {
        expect(res.category).toBe("Validation");
      }
    }
  });

  it("manifestWriter_setVaultPreserveExistingSecretsAndRemoveVault_sealsCompactsAndPreservesSiblings", async () => {
    const keyring = new InMemoryKeyring();

    expect(
      ConfigurationManifestWriter.setConfigurationDirectory(manifestPath, "OtherApp", "/other/dir").ok
    ).toBe(true);

    const setRes1 = await ConfigurationManifestWriter.setVault(
      manifestPath,
      {
        name: "bws-prod",
        kind: "bitwarden-secrets",
        endpoint: "https://api.bitwarden.com",
        cacheTTLSeconds: 60,
        settings: { projectId: "proj-prod-1" },
        secrets: {
          accessToken: "bws-secret-token-v1",
          secondaryToken: "bws-secondary-v1"
        }
      },
      { keyring }
    );
    expect(setRes1.ok).toBe(true);
    if (setRes1.ok) {
      expect(String(setRes1.value.secrets?.["accessToken"])).toBe("ManifestSecretEnvelope { [REDACTED] }");
    }

    // Verify compact on-disk representation: no redundant "name", encrypted envelopes, no plaintext
    const rawDiskText = fs.readFileSync(manifestPath, "utf8");
    expect(rawDiskText).not.toContain("bws-secret-token-v1");
    expect(rawDiskText).not.toContain("bws-secondary-v1");
    const parsedDisk1 = JSON.parse(rawDiskText) as {
      keyVaults: Record<string, Record<string, unknown>>;
      applications: Record<string, unknown>;
    };
    expect(Object.prototype.hasOwnProperty.call(parsedDisk1.keyVaults["bws-prod"]!, "name")).toBe(false);
    expect(parsedDisk1.applications["OtherApp"]).toBeDefined();

    // Update settings & TTL with preserveExistingSecrets: true and NO secrets -> keyring is not even needed!
    const unreachableKeyring = new InMemoryKeyring();
    unreachableKeyring.setUnreachable(true);
    const setRes2 = await ConfigurationManifestWriter.setVault(
      manifestPath,
      {
        name: "bws-prod",
        kind: "bitwarden-secrets",
        cacheTTLSeconds: 120,
        settings: { projectId: "proj-prod-2" }
      },
      { keyring: unreachableKeyring, preserveExistingSecrets: true }
    );
    expect(setRes2.ok).toBe(true);
    if (setRes2.ok) {
      expect(Object.keys(setRes2.value.secrets ?? {}).sort()).toEqual(["accessToken", "secondaryToken"]);
      expect(setRes2.value.cacheTTLSeconds).toBe(120);
    }

    // Rotate accessToken while preserving secondaryToken
    const setRes3 = await ConfigurationManifestWriter.setVault(
      manifestPath,
      {
        name: "bws-prod",
        kind: "bitwarden-secrets",
        cacheTTLSeconds: 120,
        settings: { projectId: "proj-prod-2" },
        secrets: { accessToken: "bws-secret-token-v2" }
      },
      { keyring, preserveExistingSecrets: true }
    );
    expect(setRes3.ok).toBe(true);
    if (setRes3.ok && setRes1.ok) {
      expect(setRes3.value.secrets?.["secondaryToken"]?.value).toBe(
        setRes1.value.secrets?.["secondaryToken"]?.value
      );
      expect(setRes3.value.secrets?.["accessToken"]?.value).not.toBe(
        setRes1.value.secrets?.["accessToken"]?.value
      );
    }

    // setConfigurationDirectory preserves non-empty root keyVaults
    expect(
      ConfigurationManifestWriter.setConfigurationDirectory(manifestPath, "Cool-CLI", "new-dir").ok
    ).toBe(true);
    const readAfterAppWrite = ConfigurationManifestReader.readManifest(manifestPath);
    expect(readAfterAppWrite.ok).toBe(true);
    if (readAfterAppWrite.ok) {
      expect(readAfterAppWrite.value.keyVaults?.["bws-prod"]?.settings?.["projectId"]).toBe("proj-prod-2");
    }

    // removeVault removes bws-prod and omits empty root "keyVaults" on disk
    const rmRes1 = ConfigurationManifestWriter.removeVault(manifestPath, "bws-prod");
    expect(rmRes1.ok && rmRes1.value).toBe(true);

    const parsedAfterRemove = JSON.parse(fs.readFileSync(manifestPath, "utf8")) as Record<string, unknown>;
    expect(Object.prototype.hasOwnProperty.call(parsedAfterRemove, "keyVaults")).toBe(false);

    // Removing again returns ok(false)
    const rmRes2 = ConfigurationManifestWriter.removeVault(manifestPath, "bws-prod");
    expect(rmRes2.ok && rmRes2.value).toBe(false);
  });
});
