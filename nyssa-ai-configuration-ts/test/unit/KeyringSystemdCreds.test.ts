import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { KeyringSystemdCreds } from "../../src/keyring/KeyringSystemdCreds.js";
import { KeyringProtectedFile } from "../../src/keyring/KeyringProtectedFile.js";
import { ConfigurationBootstrap } from "../../src/ConfigurationBootstrap.js";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";

describe("KeyringSystemdCreds", () => {
  let tempDir: string;
  let credsDir: string;
  let originalCredentialsDir: string | undefined;
  let originalNyssaConfigDir: string | undefined;

  beforeEach(() => {
    originalCredentialsDir = process.env["CREDENTIALS_DIRECTORY"];
    originalNyssaConfigDir = process.env["NYSSA_CONFIG_DIR"];
    delete process.env["CREDENTIALS_DIRECTORY"];
    delete process.env["NYSSA_CONFIG_DIR"];

    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-systemd-creds-test-"));
    credsDir = path.join(tempDir, "credentials");
    fs.mkdirSync(credsDir, { recursive: true });
  });

  afterEach(() => {
    if (originalCredentialsDir === undefined) {
      delete process.env["CREDENTIALS_DIRECTORY"];
    } else {
      process.env["CREDENTIALS_DIRECTORY"] = originalCredentialsDir;
    }

    if (originalNyssaConfigDir === undefined) {
      delete process.env["NYSSA_CONFIG_DIR"];
    } else {
      process.env["NYSSA_CONFIG_DIR"] = originalNyssaConfigDir;
    }

    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore cleanup errors
    }
  });

  it("keyringSystemdCreds_isAvailable_reportsPresenceAndAbsenceOfCredentialsDirectory", () => {
    // Arrange: CREDENTIALS_DIRECTORY is unset
    const keyring = new KeyringSystemdCreds();

    // Act & Assert: unset
    expect(KeyringSystemdCreds.isAvailable()).toBe(false);
    expect(keyring.isAvailable()).toBe(false);

    // Act & Assert: set to non-existent directory
    process.env["CREDENTIALS_DIRECTORY"] = path.join(tempDir, "missing-dir");
    expect(KeyringSystemdCreds.isAvailable()).toBe(false);
    expect(keyring.isAvailable()).toBe(false);

    // Act & Assert: set to existing directory
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    expect(KeyringSystemdCreds.isAvailable()).toBe(true);
    expect(keyring.isAvailable()).toBe(true);
  });

  it("keyringSystemdCreds_getBase64Key_readsAndParses32ByteKeyWithWhitespace", async () => {
    // Arrange
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const expectedKey = crypto.randomBytes(32);
    const credFile = path.join(credsDir, "OrdersService.key");
    fs.writeFileSync(credFile, `  ${expectedKey.toString("base64")}\n`, "utf8");
    const keyring = new KeyringSystemdCreds();

    // Act
    const res = await keyring.get("nyssa-ai-configuration", "OrdersService");

    // Assert
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value.equals(expectedKey)).toBe(true);
    }
  });

  it("keyringSystemdCreds_getRawBinaryKey_reads32ByteBinaryMaterial", async () => {
    // Arrange
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const expectedKey = crypto.randomBytes(32);
    const credFile = path.join(credsDir, "nyssa-ai-configuration.key");
    fs.writeFileSync(credFile, expectedKey);
    const keyring = new KeyringSystemdCreds();

    // Act
    const res = await keyring.get("nyssa-ai-configuration", "OrdersService");

    // Assert
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value.equals(expectedKey)).toBe(true);
    }
  });

  it("keyringSystemdCreds_getSingleFallbackFile_usesOnlyCredentialFileInDirectory", async () => {
    // Arrange
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const expectedKey = crypto.randomBytes(32);
    const credFile = path.join(credsDir, "systemd-provisioned-secret");
    fs.writeFileSync(credFile, expectedKey.toString("base64"), "utf8");
    const keyring = new KeyringSystemdCreds();

    // Act
    const res = await keyring.get("nyssa-ai-configuration", "HeadlessWorker");

    // Assert
    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value.equals(expectedKey)).toBe(true);
    }
  });

  it("keyringSystemdCreds_getMissingFile_returnsNotFoundResult", async () => {
    // Arrange
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const keyring = new KeyringSystemdCreds();

    // Act
    const res = await keyring.get("nyssa-ai-configuration", "MissingApp");

    // Assert
    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.category).toBe("NotFound");
    }
  });

  it.each([
    ["empty file", ""],
    ["whitespace only", "   \n\t  "],
    ["malformed base64 characters", "not-valid-base64!!!@@@###$$$"],
    ["16-byte base64 key (too short)", Buffer.alloc(16, 0x11).toString("base64")],
    ["64-byte base64 key (too long)", Buffer.alloc(64, 0x22).toString("base64")]
  ])(
    "keyringSystemdCreds_getInvalidKeyMaterial_%s_returnsValidationError",
    async (_label, invalidContent) => {
      // Arrange
      process.env["CREDENTIALS_DIRECTORY"] = credsDir;
      fs.writeFileSync(path.join(credsDir, "OrdersService.key"), invalidContent, "utf8");
      const keyring = new KeyringSystemdCreds();

      // Act
      const res = await keyring.get("nyssa-ai-configuration", "OrdersService");

      // Assert
      expect(res.ok).toBe(false);
      if (!res.ok) {
        expect(res.category).toBe("Validation");
        expect(res.message.length).toBeGreaterThan(0);
      }
    }
  );

  it("keyringSystemdCreds_setAndDelete_returnUnsupportedError", async () => {
    // Arrange
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const keyring = new KeyringSystemdCreds();
    const secret = crypto.randomBytes(32);

    // Act
    const setRes = await keyring.set("nyssa-ai-configuration", "OrdersService", secret);
    const delRes = await keyring.delete("nyssa-ai-configuration", "OrdersService");

    // Assert
    expect(setRes.ok).toBe(false);
    if (!setRes.ok) {
      expect(setRes.category).toBe("Forbidden");
      expect(setRes.message).toBe(
        "systemd-creds is immutable at runtime; credentials must be provisioned via systemd service configuration."
      );
    }

    expect(delRes.ok).toBe(false);
    if (!delRes.ok) {
      expect(delRes.category).toBe("Forbidden");
      expect(delRes.message).toBe(
        "systemd-creds is immutable at runtime; credentials must be provisioned via systemd service configuration."
      );
    }
  });

  it("configurationBootstrap_whenCredentialsDirectorySet_selectsKeyringSystemdCredsAndEncryptsSecrets", async () => {
    // Arrange
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const masterKey = crypto.randomBytes(32);
    fs.writeFileSync(
      path.join(credsDir, "custom-systemd-cred"),
      masterKey.toString("base64"),
      "utf8"
    );

    const manifestPath = path.join(tempDir, "config-manifest.json");
    const configDir = path.join(tempDir, "config");

    // Act
    const bootRes = await ConfigurationBootstrap.createStore({
      applicationName: "SystemdServiceApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      systemdCredentialName: "custom-systemd-cred"
    });

    // Assert: Bootstrap selected KeyringSystemdCreds and did not create a local .nyssa-key file
    expect(bootRes.ok).toBe(true);
    if (!bootRes.ok) return;

    expect(bootRes.value.keyring).toBeInstanceOf(KeyringSystemdCreds);
    expect(fs.existsSync(path.join(configDir, KeyringProtectedFile.KEY_FILENAME))).toBe(false);

    // Verify end-to-end encryption and decryption using the systemd-creds master key
    const clientRes = await ConfigurationClient.initialize({
      applicationName: "SystemdServiceApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      systemdCredentialName: "custom-systemd-cred"
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;

    const setRes = await clientRes.value.set("database", "password", "systemd-protected-secret", {
      encrypted: true,
      provider: "aesLocal"
    });
    expect(setRes.ok).toBe(true);

    const getRes = await clientRes.value.get("database", "password");
    expect(getRes.ok).toBe(true);
    if (getRes.ok) {
      expect(getRes.value).toBe("systemd-protected-secret");
    }
  });

  it("configurationBootstrap_resolveKeyring_fallsBackToProtectedFileWhenNyssaKeyExists", () => {
    // Arrange
    const configDir = path.join(tempDir, "config-protected");
    fs.mkdirSync(configDir, { recursive: true });
    fs.writeFileSync(
      path.join(configDir, KeyringProtectedFile.KEY_FILENAME),
      crypto.randomBytes(32).toString("base64"),
      "utf8"
    );

    // Act
    const keyring = ConfigurationBootstrap.resolveKeyring(configDir);

    // Assert
    expect(keyring).toBeInstanceOf(KeyringProtectedFile);
  });
});
