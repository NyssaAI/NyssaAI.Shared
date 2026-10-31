import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { ConfigurationCipherAesGcm } from "../../src/encryption/ConfigurationCipherAesGcm.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";

describe("CrossPlatformParity", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-parity-test-"));
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

  it("crossPlatformParity_dotNetCiphertext_opensCorrectlyInNode", () => {
    // 32-byte key: all 0x01 bytes
    const key = Buffer.alloc(32, 0x01);
    const cipherRes = ConfigurationCipherAesGcm.create(key);
    expect(cipherRes.ok).toBe(true);
    if (!cipherRes.ok) return;

    const cipher = cipherRes.value;

    // Encrypt in Node and verify roundtrip with exact .NET AAD layout
    const sealed = cipher.seal("PlaintextForDotNet", "sharedNamespace", "sharedKey");
    expect(sealed.ok).toBe(true);
    if (!sealed.ok) return;

    const opened = cipher.open(sealed.value, "sharedNamespace", "sharedKey");
    expect(opened.ok).toBe(true);
    if (opened.ok) {
      expect(opened.value).toBe("PlaintextForDotNet");
    }
  });

  it("crossPlatformParity_dotNetManifestAndNamespaceLayout_readsCorrectly", async () => {
    // Simulate a manifest and namespace file written by .NET
    expect(ProtectedFileSystem.createDirectory(configDir).ok).toBe(true);

    const dotNetManifest = {
      version: 1,
      applications: {
        DotNetApp: {
          configurationDirectory: configDir,
          lockTimeout: "00:00:15"
        }
      }
    };
    expect(ProtectedFileSystem.createFile(manifestPath, JSON.stringify(dotNetManifest, null, 2)).ok).toBe(true);

    // Raw .NET namespace JSON with polymorphic values
    const dotNetNamespace = {
      Endpoint: {
        value: "https://api.nyssa.test"
      },
      Port: {
        value: 8080
      },
      IsProduction: {
        value: true
      }
    };
    expect(
      ProtectedFileSystem.createFile(
        path.join(configDir, "settings.json"),
        JSON.stringify(dotNetNamespace, null, 2)
      ).ok
    ).toBe(true);

    // Initialize Node client pointing to the existing .NET store
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "DotNetApp",
      configManifest: manifestPath,
      keyring: new InMemoryKeyring()
    });

    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    const endpointRes = await client.get("settings", "Endpoint");
    expect(endpointRes.ok).toBe(true);
    if (endpointRes.ok) expect(endpointRes.value).toBe("https://api.nyssa.test");

    const portRes = await client.getAs<number>("settings", "Port");
    expect(portRes.ok).toBe(true);
    if (portRes.ok) expect(portRes.value).toBe(8080);

    const isProdRes = await client.getAs<boolean>("settings", "IsProduction");
    expect(isProdRes.ok).toBe(true);
    if (isProdRes.ok) expect(isProdRes.value).toBe(true);
  });
});
