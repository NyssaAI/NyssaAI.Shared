import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { execFileSync } from "node:child_process";
import { ConfigurationManifestReader } from "../../src/manifest/ConfigurationManifestReader.js";
import { ConfigurationManifestWriter } from "../../src/manifest/ConfigurationManifestWriter.js";
import { TimeSpanParser } from "../../src/manifest/TimeSpanParser.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";

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

  it("manifestWriter_newApplication_provisionsSection", () => {
    const configDir = path.join(tempDir, "app-config");
    const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
      manifestPath,
      "Cool-CLI",
      configDir,
      5000
    );

    expect(writeRes.ok).toBe(true);
    expect(fs.existsSync(manifestPath)).toBe(true);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value.configurationDirectory).toBe(path.resolve(configDir));
      expect(readRes.value.lockTimeoutMs).toBe(5000);
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

  it("manifestReader_withCommentsAndTrailingCommas_parsesVaultAndTimeout", () => {
    const annotatedJsonc = `{
      // Manifest version
      "version": 1,
      "applications": {
        "Cool-CLI": {
          "configurationDirectory": "Cool-CLI",
          "lockTimeout": "00:00:02",
          /* Cloud vault configuration */
          "keyVault": {
            "name": "corpVault",
            "kind": "azure-key-vault",
            "credential": "vault/token",
            "settings": {
              "vaultUri": "https://corp.vault.azure.net/",
            },
          },
        },
      },
    }`;
    ProtectedFileSystem.createFile(manifestPath, annotatedJsonc);

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value.configurationDirectory).toBe(path.resolve(tempDir, "Cool-CLI"));
      expect(readRes.value.lockTimeoutMs).toBe(2000);
      expect(readRes.value.keyVault?.name).toBe("corpVault");
      expect(readRes.value.keyVault?.credential).toBe("vault/token");
      expect(readRes.value.keyVault?.settings).toEqual({
        vaultUri: "https://corp.vault.azure.net/"
      });
      expect(readRes.value.keyVault?.secrets).toEqual({});
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

  it("manifestReader_secretsInReadableFile_failsForbiddenWithoutLeakingSecret", () => {
    const manifestWithSecrets = {
      version: 1,
      applications: {
        "Cool-CLI": {
          configurationDirectory: "Cool-CLI",
          keyVault: {
            name: "corp",
            kind: "azure-key-vault",
            secrets: {
              clientSecret: "fictional"
            }
          }
        }
      }
    };
    ProtectedFileSystem.createFile(manifestPath, JSON.stringify(manifestWithSecrets, null, 2));

    if (process.platform === "win32") {
      execFileSync("icacls.exe", [manifestPath, "/grant", "*S-1-1-0:(R)"], { stdio: "ignore" });
    } else {
      fs.chmodSync(manifestPath, 0o644);
    }

    const readRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(readRes.ok).toBe(false);
    if (!readRes.ok) {
      expect(readRes.category).toBe("Forbidden");
      expect(readRes.error).toContain("is readable by");
      expect(readRes.error).not.toContain("fictional");
    }
  });

  it("manifestWriter_existingManifest_preservesOtherSectionsAndKeyVault", () => {
    const initialManifest = {
      version: 1,
      applications: {
        OtherApp: {
          configurationDirectory: "/other/dir"
        },
        "Cool-CLI": {
          configurationDirectory: "old-dir",
          lockTimeout: "00:00:15",
          keyVault: {
            name: "corp",
            kind: "in-memory"
          }
        }
      }
    };
    ProtectedFileSystem.createFile(manifestPath, JSON.stringify(initialManifest, null, 2));

    const writeRes = ConfigurationManifestWriter.setConfigurationDirectory(
      manifestPath,
      "Cool-CLI",
      "new-dir"
    );
    expect(writeRes.ok).toBe(true);

    const otherRes = ConfigurationManifestReader.readApplication(manifestPath, "OtherApp");
    expect(otherRes.ok).toBe(true);

    const updatedRes = ConfigurationManifestReader.readApplication(manifestPath, "Cool-CLI");
    expect(updatedRes.ok).toBe(true);
    if (updatedRes.ok) {
      expect(updatedRes.value.configurationDirectory).toBe(path.resolve(tempDir, "new-dir"));
      expect(updatedRes.value.lockTimeoutMs).toBe(15000);
      expect(updatedRes.value.keyVault?.name).toBe("corp");
    }
  });
});
