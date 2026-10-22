import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { ConfigurationStore } from "../../src/values/ConfigurationStore.js";
import { ConfigurationCipherAesGcm } from "../../src/encryption/ConfigurationCipherAesGcm.js";

describe("ConfigurationStore and Document Locking", () => {
  let tempDir: string;
  let cipher: ConfigurationCipherAesGcm;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-store-test-"));
    const key = crypto.randomBytes(32);
    const cipherRes = ConfigurationCipherAesGcm.create(key);
    if (!cipherRes.ok) throw new Error("cipher creation failed");
    cipher = cipherRes.value;
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("configurationStore_plainValues_storesAndRetrievesDirectly", async () => {
    const store = new ConfigurationStore(tempDir, 5000, cipher);

    const setRes = await store.set("mail", "host", "smtp.example.com");
    expect(setRes.ok).toBe(true);

    const getRes = await store.get("mail", "host");
    expect(getRes.ok).toBe(true);
    if (getRes.ok) {
      expect(getRes.value).toBe("smtp.example.com");
    }
  });

  it("configurationStore_encryptedValues_sealsAndUnsealsWithAesGcm", async () => {
    const store = new ConfigurationStore(tempDir, 5000, cipher);

    const setRes = await store.set("database", "password", "SuperSecret123!", true, "aesLocal");
    expect(setRes.ok).toBe(true);

    // Verify raw file on disk is encrypted
    const filePath = path.join(tempDir, "database.json");
    const raw = fs.readFileSync(filePath, "utf8");
    expect(raw).not.toContain("SuperSecret123!");
    expect(raw).toContain("aesLocal");

    // Retrieve through store
    const getRes = await store.get("database", "password");
    expect(getRes.ok).toBe(true);
    if (getRes.ok) {
      expect(getRes.value).toBe("SuperSecret123!");
    }
  });

  it("configurationStore_removeKey_deletesEntryFromDocument", async () => {
    const store = new ConfigurationStore(tempDir, 5000, cipher);

    await store.set("app", "tempKey", "val");
    const removeRes = await store.remove("app", "tempKey");
    expect(removeRes.ok).toBe(true);
    if (removeRes.ok) expect(removeRes.value).toBe(true);

    const getRes = await store.get("app", "tempKey");
    expect(getRes.ok).toBe(false);
  });
});
