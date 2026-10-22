import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { KeyringProtectedFile } from "../../src/keyring/KeyringProtectedFile.js";
import { KeyringDefault } from "../../src/keyring/KeyringDefault.js";

describe("Keyring", () => {
  let tempDir: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-keyring-test-"));
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("keyringProtectedFile_createNew_sets0600Mode", async () => {
    const keyring = new KeyringProtectedFile(tempDir);
    const key = Buffer.alloc(32, 0x42);

    const setRes = await keyring.set("TestApp", "configuration-key", key);
    expect(setRes.ok).toBe(true);

    const keyFilePath = path.join(tempDir, KeyringProtectedFile.KEY_FILENAME);
    expect(fs.existsSync(keyFilePath)).toBe(true);

    if (process.platform !== "win32") {
      const mode = fs.statSync(keyFilePath).mode & 0o777;
      expect(mode).toBe(0o600);
    }
  });

  it("keyring_getOrCreate_generatesAndRetrievesConsistentKey", async () => {
    const keyring = new KeyringProtectedFile(tempDir);

    const firstRes = await KeyringDefault.getOrCreateKey("Cool-CLI", keyring);
    expect(firstRes.ok).toBe(true);
    if (!firstRes.ok) return;

    expect(firstRes.value.length).toBe(32);

    // Second call should return the exact same key bytes
    const secondRes = await KeyringDefault.getOrCreateKey("Cool-CLI", keyring);
    expect(secondRes.ok).toBe(true);
    if (!secondRes.ok) return;

    expect(secondRes.value.equals(firstRes.value)).toBe(true);
  });
});
