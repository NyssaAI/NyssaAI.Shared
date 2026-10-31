import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";

describe("NamespaceSettings Public Use Cases (F-102)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  let keyring: InMemoryKeyring;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-ns-settings-"));
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

  it("namespaceSettings_setNamespaceAndGetNamespace_roundTripsWholeObjectAndReplacesOmittedKeys (F-102 UC-001 & UC-002)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "NsApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    interface SmtpSettings {
      host: string;
      port: number;
      enableSsl: boolean;
      legacyHeader?: string;
    }

    const firstWrite = await client.setNamespace<SmtpSettings>("smtp", {
      host: "smtp.example.com",
      port: 587,
      enableSsl: true,
      legacyHeader: "X-Legacy"
    });
    expect(firstWrite.ok).toBe(true);

    // Replace namespace without legacyHeader
    const secondWrite = await client.setNamespace<SmtpSettings>("smtp", {
      host: "smtp.new.example.com",
      port: 465,
      enableSsl: true
    });
    expect(secondWrite.ok).toBe(true);

    const readRes = await client.getNamespace<SmtpSettings>("smtp");
    expect(readRes.ok).toBe(true);
    if (readRes.ok) {
      expect(readRes.value).toEqual({
        host: "smtp.new.example.com",
        port: 465,
        enableSsl: true
      });
      expect(readRes.value.legacyHeader).toBeUndefined();
    }
  });

  it("namespaceSettings_mergeSettings_updatesProvidedKeysAndPreservesUnmentionedAndEncryptedKeys (F-102 UC-003)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "NsApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Seed an encrypted key and two plain keys
    expect(
      (
        await client.set("service", "apiSecret", "top-secret-v1", {
          encrypted: true,
          provider: "aesLocal"
        })
      ).ok
    ).toBe(true);
    expect((await client.set("service", "endpoint", "https://v1.example.com")).ok).toBe(true);
    expect((await client.setAs("service", "timeoutSeconds", 30)).ok).toBe(true);

    // Partial merge updates endpoint and rotates apiSecret while preserving timeoutSeconds and apiSecret encryption
    const mergeRes = await client.mergeSettings("service", {
      endpoint: "https://v2.example.com",
      apiSecret: "top-secret-v2",
      ignoredUndefined: undefined
    });
    expect(mergeRes.ok).toBe(true);

    const rawJson = fs.readFileSync(path.join(configDir, "service.json"), "utf8");
    expect(rawJson).not.toContain("top-secret-v2");

    const nsRes = await client.getNamespace<{
      apiSecret: string;
      endpoint: string;
      timeoutSeconds: number;
    }>("service");
    expect(nsRes.ok).toBe(true);
    if (nsRes.ok) {
      expect(nsRes.value).toEqual({
        apiSecret: "top-secret-v2",
        endpoint: "https://v2.example.com",
        timeoutSeconds: 30
      });
    }
  });

  it("namespaceSettings_stringFidelityAndNestedObjects_preservesLeadingZerosQuotesAndNestedStructures (F-102 UC-004 & UC-005)", async () => {
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "NsApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Leading zeros, boolean-like strings, quoted strings, and JSON-looking strings stay exact strings
    expect((await client.set("fidelity", "zipCode", "00501")).ok).toBe(true);
    expect((await client.set("fidelity", "flagString", "true")).ok).toBe(true);
    expect((await client.set("fidelity", "quoted", "\"hello\"")).ok).toBe(true);
    expect((await client.set("fidelity", "embeddedJson", "{\"nested\":1}")).ok).toBe(true);

    const zipRes = await client.get("fidelity", "zipCode");
    expect(zipRes.ok && zipRes.value).toBe("00501");

    const flagRes = await client.get("fidelity", "flagString");
    expect(flagRes.ok && flagRes.value).toBe("true");

    const quotedRes = await client.get("fidelity", "quoted");
    expect(quotedRes.ok && quotedRes.value).toBe("\"hello\"");

    const embeddedRes = await client.get("fidelity", "embeddedJson");
    expect(embeddedRes.ok && embeddedRes.value).toBe("{\"nested\":1}");

    // Nested objects, arrays, nulls, and empty objects via setNamespace / getNamespace
    interface ComplexConfig {
      nested: {
        retries: number;
        tags: string[];
        emptyObj: Record<string, never>;
      };
      nullableField: string | null;
    }

    const complexPayload: ComplexConfig = {
      nested: {
        retries: 3,
        tags: ["primary", "backup"],
        emptyObj: {}
      },
      nullableField: null
    };

    expect((await client.setNamespace("complex", complexPayload)).ok).toBe(true);
    const complexRead = await client.getNamespace<ComplexConfig>("complex");
    expect(complexRead.ok).toBe(true);
    if (complexRead.ok) {
      expect(complexRead.value).toEqual(complexPayload);
    }

    // Non-object passed to mergeSettings is rejected with Validation
    const badMerge = await client.mergeSettings("complex", ["not", "an", "object"] as unknown);
    expect(badMerge.ok).toBe(false);
    if (!badMerge.ok) {
      expect(badMerge.category).toBe("Validation");
    }
  });
});
