import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import type { IConfigurationLogger, LogRecord } from "../../src/logging/IConfigurationLogger.js";

describe("ConfigurationClient Public Use-Case Workflow", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  const capturedLogs: LogRecord[] = [];

  const testLogger: IConfigurationLogger = {
    log(record: LogRecord) {
      capturedLogs.push(record);
    }
  };

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-client-test-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
    configDir = path.join(tempDir, "config");
    capturedLogs.length = 0;
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("configurationClient_endToEndWorkflow_persistsAndRetrievesSettings", async () => {
    // Arrange: Initialize client with custom manifest and config directories
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "Cool-CLI",
      configManifest: manifestPath,
      configDirectory: configDir,
      logger: testLogger
    });

    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Act 1: Set plain string
    const setHostRes = await client.set("mail", "host", "smtp.office365.com");
    expect(setHostRes.ok).toBe(true);

    // Act 2: Set encrypted password (sealed using AES-256-GCM)
    const setPassRes = await client.set("mail", "password", "SuperSecretP@ss!", {
      encrypted: true,
      provider: "aesLocal"
    });
    expect(setPassRes.ok).toBe(true);

    // Act 3: Set complex object
    interface MailOptions {
      port: number;
      useSsl: boolean;
    }
    const setObjRes = await client.setAs<MailOptions>("mail", "options", {
      port: 587,
      useSsl: true
    });
    expect(setObjRes.ok).toBe(true);

    // Assert: Read back across a fresh client instance simulating a restart
    const freshClientRes = await ConfigurationClient.initialize({
      applicationName: "Cool-CLI",
      configManifest: manifestPath,
      logger: testLogger
    });
    expect(freshClientRes.ok).toBe(true);
    if (!freshClientRes.ok) return;
    const freshClient = freshClientRes.value;

    const hostRes = await freshClient.get("mail", "host");
    expect(hostRes.ok).toBe(true);
    if (hostRes.ok) expect(hostRes.value).toBe("smtp.office365.com");

    const passRes = await freshClient.get("mail", "password");
    expect(passRes.ok).toBe(true);
    if (passRes.ok) expect(passRes.value).toBe("SuperSecretP@ss!");

    const objRes = await freshClient.getAs<MailOptions>("mail", "options");
    expect(objRes.ok).toBe(true);
    if (objRes.ok) {
      expect(objRes.value.port).toBe(587);
      expect(objRes.value.useSsl).toBe(true);
    }

    // Verify operational logs emitted without secrets
    expect(capturedLogs.length).toBeGreaterThan(0);
    const serialized = JSON.stringify(capturedLogs);
    expect(serialized).not.toContain("SuperSecretP@ss!");
    expect(serialized).toContain("configuration.set.completed");
    expect(serialized).toContain("configuration.get.completed");
  });
});
