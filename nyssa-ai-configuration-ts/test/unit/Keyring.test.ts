import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { KeyringProtectedFile } from "../../src/keyring/KeyringProtectedFile.js";
import { KeyringDefault } from "../../src/keyring/KeyringDefault.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { KeyringOsPlatform } from "../../src/keyring/KeyringOsPlatform.js";

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
    const keyring = new InMemoryKeyring();

    const firstRes = await KeyringDefault.getOrCreateKey("Cool-CLI", keyring);
    expect(firstRes.ok).toBe(true);
    if (!firstRes.ok) return;

    expect(firstRes.value.length).toBe(32);
    expect(keyring.writes.length).toBe(1);

    // Second call should return the exact same key bytes without writing again
    const secondRes = await KeyringDefault.getOrCreateKey("Cool-CLI", keyring);
    expect(secondRes.ok).toBe(true);
    if (!secondRes.ok) return;

    expect(secondRes.value.equals(firstRes.value)).toBe(true);
    expect(keyring.writes.length).toBe(1);
  });

  it("keyringDefault_unreachableStore_failsWithoutCreatingKey", async () => {
    const keyring = new InMemoryKeyring();
    keyring.setUnreachable(true);

    const res = await KeyringDefault.getOrCreateKey("Cool-CLI", keyring);
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.category).toBe("Unavailable");
    }
    expect(keyring.writes.length).toBe(0);
  });

  it("keyring_deleteStoredKey_removesOnlyAddressedEntry", async () => {
    const keyring = new InMemoryKeyring();
    const keyA = crypto.randomBytes(32);
    const keyB = crypto.randomBytes(32);

    await keyring.set("ServiceA", "configuration-key", keyA);
    await keyring.set("ServiceB", "configuration-key", keyB);

    const delRes = await keyring.delete("ServiceA", "configuration-key");
    expect(delRes.ok).toBe(true);

    const getARes = await keyring.get("ServiceA", "configuration-key");
    expect(getARes.ok).toBe(true);
    if (getARes.ok) expect(getARes.value).toBeNull();

    const getBRes = await keyring.get("ServiceB", "configuration-key");
    expect(getBRes.ok).toBe(true);
    if (getBRes.ok) expect(getBRes.value?.equals(keyB)).toBe(true);

    // Deleting an absent key succeeds idempotently
    const delAgain = await keyring.delete("ServiceA", "configuration-key");
    expect(delAgain.ok).toBe(true);
  });

  it.skipIf(process.env["NYSSA_OS_KEYRING_TESTS"] !== "1")(
    "keyringOsPlatform_roundTripAndDelete_succeedsOnLiveKeychain",
    async () => {
      const keyring = new KeyringOsPlatform();
      const service = `nyssa-node-test-${crypto.randomUUID()}`;
      const account = "configuration-key";
      const secret = crypto.randomBytes(32);

      try {
        const initialGet = await keyring.get(service, account);
        expect(initialGet.ok).toBe(true);
        if (initialGet.ok) expect(initialGet.value).toBeNull();

        const setRes = await keyring.set(service, account, secret);
        expect(setRes.ok).toBe(true);

        const getRes = await keyring.get(service, account);
        expect(getRes.ok).toBe(true);
        if (getRes.ok) expect(getRes.value?.equals(secret)).toBe(true);

        const delRes = await keyring.delete(service, account);
        expect(delRes.ok).toBe(true);

        const afterDel = await keyring.get(service, account);
        expect(afterDel.ok).toBe(true);
        if (afterDel.ok) expect(afterDel.value).toBeNull();
      } finally {
        await keyring.delete(service, account);
      }
    }
  );
});
