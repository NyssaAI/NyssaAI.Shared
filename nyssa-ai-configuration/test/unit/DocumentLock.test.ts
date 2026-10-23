import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { ConfigurationStore } from "../../src/values/ConfigurationStore.js";
import { ConfigurationDocumentLocalFile } from "../../src/values/ConfigurationDocumentLocalFile.js";
import { ConfigurationCipherAesGcm } from "../../src/encryption/ConfigurationCipherAesGcm.js";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { ok } from "../../src/results/Result.js";

describe("ConfigurationStore and Document Locking", () => {
  let tempDir: string;
  let cipher: ConfigurationCipherAesGcm;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-store-test-"));
    ProtectedFileSystem.createDirectory(tempDir);
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

    const filePath = path.join(tempDir, "database.json");
    const raw = fs.readFileSync(filePath, "utf8");
    expect(raw).not.toContain("SuperSecret123!");
    expect(raw).toContain("aesLocal");

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

    const getRes = await store.get("app", "tempKey");
    expect(getRes.ok).toBe(false);
  });

  it("documentLocalFile_concurrentWriters_preservesBothKeys", async () => {
    const docPath = path.join(tempDir, "concurrent.json");
    const docA = new ConfigurationDocumentLocalFile(docPath, 5000);
    const docB = new ConfigurationDocumentLocalFile(docPath, 5000);

    const [resA, resB] = await Promise.all([
      docA.modify(async (entries) => {
        await new Promise((r) => setTimeout(r, 30));
        entries["keyA"] = { value: "valueA" };
        return ok(entries);
      }),
      docB.modify(async (entries) => {
        await new Promise((r) => setTimeout(r, 10));
        entries["keyB"] = { value: "valueB" };
        return ok(entries);
      })
    ]);

    expect(resA.ok).toBe(true);
    expect(resB.ok).toBe(true);

    const readRes = await docA.read();
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value["keyA"]?.value).toBe("valueA");
      expect(readRes.value["keyB"]?.value).toBe("valueB");
    }
  });

  it("documentLocalFile_lockHeldPastTimeout_returnsUnavailable", async () => {
    const docPath = path.join(tempDir, "contended.json");
    const holder = new ConfigurationDocumentLocalFile(docPath, 5000);
    const waiter = new ConfigurationDocumentLocalFile(docPath, 60);

    let releaseHolder!: () => void;
    const holderDone = holder.modify(async (entries) => {
      await new Promise<void>((r) => {
        releaseHolder = r;
      });
      entries["held"] = { value: "yes" };
      return ok(entries);
    });

    await new Promise((r) => setTimeout(r, 15));
    const waiterRes = await waiter.read();
    expect(waiterRes.ok).toBe(false);
    if (!waiterRes.ok) {
      expect(waiterRes.category).toBe("Unavailable");
    }

    releaseHolder();
    await holderDone;
  });

  it("validateSegment_pathTraversalAndReservedNames_failsValidation", () => {
    expect(ConfigurationDocumentLocalFile.validateSegment("../secret", "namespace").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("a/b", "namespace").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("a\\b", "namespace").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment(".pending", "namespace").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("CON", "namespace").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("config-manifest", "namespace").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("../key", "key").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("a/b", "key").ok).toBe(false);
    expect(ConfigurationDocumentLocalFile.validateSegment("valid-ns_1.0", "namespace").ok).toBe(true);
  });
});
