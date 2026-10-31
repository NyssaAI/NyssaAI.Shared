import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";

describe("BasicSettings Public Use Cases (F-101)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  let keyring: InMemoryKeyring;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-basic-settings-"));
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

  it("basicSettings_setGetListRemovePlaintext_completesCrudAndOrdinalList (F-101 UC-001)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "BasicApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Missing namespace list returns []
    const emptyListRes = await client.list("app");
    expect(emptyListRes.ok).toBe(true);
    if (emptyListRes.ok) {
      expect(emptyListRes.value).toEqual([]);
    }

    // Set keys out of alphabetical order
    expect((await client.set("app", "zebra", "last")).ok).toBe(true);
    expect((await client.set("app", "Alpha", "first")).ok).toBe(true);
    expect((await client.set("app", "beta", "middle")).ok).toBe(true);

    // List returns ordinal-sorted keys ("Alpha" < "beta" < "zebra")
    const listRes = await client.list("app");
    expect(listRes.ok).toBe(true);
    if (listRes.ok) {
      expect(listRes.value).toEqual(["Alpha", "beta", "zebra"]);
    }

    // Get existing key
    const getRes = await client.get("app", "beta");
    expect(getRes.ok).toBe(true);
    if (getRes.ok) {
      expect(getRes.value).toBe("middle");
    }

    // Remove existing key
    const removeRes = await client.remove("app", "beta");
    expect(removeRes.ok).toBe(true);
    if (removeRes.ok) {
      expect(removeRes.value).toEqual({
        remoteValueRemains: false
      });
    }

    // Missing key returns NotFound
    const missingRes = await client.get("app", "beta");
    expect(missingRes.ok).toBe(false);
    if (!missingRes.ok) {
      expect(missingRes.category).toBe("NotFound");
    }
  });

  it("basicSettings_encryptedSetAndOverwriteWithoutOptions_preservesEncryptionAndRejectsProviderChange (F-101 UC-002 & UC-003)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "BasicApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Initial encrypted write
    const setRes = await client.set("auth", "apiKey", "initial-secret-123", {
      encrypted: true,
      provider: "aesLocal"
    });
    expect(setRes.ok).toBe(true);

    // Overwrite without SetOptions preserves existing encrypted: true and provider: "aesLocal"
    const updateRes = await client.set("auth", "apiKey", "rotated-secret-456");
    expect(updateRes.ok).toBe(true);

    // Verify raw JSON on disk does not contain plaintext and retains encrypted: true
    const rawJson = fs.readFileSync(path.join(configDir, "auth.json"), "utf8");
    expect(rawJson).not.toContain("rotated-secret-456");
    const parsed = JSON.parse(rawJson) as Record<string, { value: string; encrypted?: boolean; provider?: string }>;
    expect(parsed["apiKey"]?.encrypted).toBe(true);
    expect(parsed["apiKey"]?.provider).toBe("aesLocal");

    // Verify decrypted read returns updated value
    const getRes = await client.get("auth", "apiKey");
    expect(getRes.ok).toBe(true);
    if (getRes.ok) {
      expect(getRes.value).toBe("rotated-secret-456");
    }

    // Attempting to change provider via set fails with Conflict instructing caller to use move()
    const conflictRes = await client.set("auth", "apiKey", "plain-attempt", {
      encrypted: false,
      provider: "Default"
    });
    expect(conflictRes.ok).toBe(false);
    if (!conflictRes.ok) {
      expect(conflictRes.category).toBe("Conflict");
      expect(conflictRes.error).toContain("use move");
    }
  });
});
