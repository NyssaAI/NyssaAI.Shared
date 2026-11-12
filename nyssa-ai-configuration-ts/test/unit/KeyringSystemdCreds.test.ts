import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { KeyringDefault } from "../../src/keyring/KeyringDefault.js";
import { KeyringOsPlatform } from "../../src/keyring/KeyringOsPlatform.js";
import { KeyringSystemdCreds } from "../../src/keyring/KeyringSystemdCreds.js";
import { KeyringProtectedFile } from "../../src/keyring/KeyringProtectedFile.js";
import { ConfigurationBootstrap } from "../../src/ConfigurationBootstrap.js";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";

describe("KeyringSystemdCreds and Manifest Key Isolation", () => {
  let tempDir: string;
  let credsDir: string;
  let originalCredentialsDir: string | undefined;
  let originalNyssaConfigDir: string | undefined;
  let originalSystemdCredsDir: string | undefined;

  beforeEach(() => {
    originalCredentialsDir = process.env["CREDENTIALS_DIRECTORY"];
    originalNyssaConfigDir = process.env["NYSSA_CONFIG_DIR"];
    originalSystemdCredsDir = process.env["NYSSA_SYSTEMD_CREDS_DIR"];
    delete process.env["CREDENTIALS_DIRECTORY"];
    delete process.env["NYSSA_CONFIG_DIR"];
    delete process.env["NYSSA_SYSTEMD_CREDS_DIR"];

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

    if (originalSystemdCredsDir === undefined) {
      delete process.env["NYSSA_SYSTEMD_CREDS_DIR"];
    } else {
      process.env["NYSSA_SYSTEMD_CREDS_DIR"] = originalSystemdCredsDir;
    }

    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore cleanup errors
    }
  });

  it("keyringSystemdCreds_isAvailable_reportsPresenceAndAbsenceOfCredentialsDirectory", () => {
    const keyring = new KeyringSystemdCreds();

    expect(KeyringSystemdCreds.isAvailable()).toBe(false);
    expect(keyring.isAvailable()).toBe(false);

    process.env["CREDENTIALS_DIRECTORY"] = path.join(tempDir, "missing-dir");
    expect(KeyringSystemdCreds.isAvailable()).toBe(false);
    expect(keyring.isAvailable()).toBe(false);

    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    expect(KeyringSystemdCreds.isAvailable()).toBe(true);
    expect(keyring.isAvailable()).toBe(true);
  });

  it("keyringSystemdCreds_getBase64Key_readsAndParses32ByteKeyWithWhitespace", async () => {
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const expectedKey = crypto.randomBytes(32);
    const credFile = path.join(credsDir, "OrdersService.key");
    fs.writeFileSync(credFile, `  ${expectedKey.toString("base64")}\n`, "utf8");
    const keyring = new KeyringSystemdCreds();

    const res = await keyring.get("nyssa-ai-configuration", "OrdersService");

    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value.equals(expectedKey)).toBe(true);
    }
  });

  it("keyringSystemdCreds_getRawBinaryKey_reads32ByteBinaryMaterial", async () => {
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const expectedKey = crypto.randomBytes(32);
    const credFile = path.join(credsDir, "nyssa-ai-configuration.key");
    fs.writeFileSync(credFile, expectedKey);
    const keyring = new KeyringSystemdCreds();

    const res = await keyring.get("nyssa-ai-configuration", "OrdersService");

    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value.equals(expectedKey)).toBe(true);
    }
  });

  it("keyringSystemdCreds_getSingleFallbackFile_usesOnlyCredentialFileInDirectory", async () => {
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const expectedKey = crypto.randomBytes(32);
    const credFile = path.join(credsDir, "systemd-provisioned-secret");
    fs.writeFileSync(credFile, expectedKey.toString("base64"), "utf8");
    const keyring = new KeyringSystemdCreds();

    const res = await keyring.get("nyssa-ai-configuration", "HeadlessWorker");

    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value.equals(expectedKey)).toBe(true);
    }
  });

  it("keyringSystemdCreds_manifestVsAppIsolation_neverCrossMatchesOrSingleFileSteals", async () => {
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const keyring = new KeyringSystemdCreds();
    const osKeyring = new KeyringOsPlatform({ platform: "linux" });
    process.env["NYSSA_SYSTEMD_CREDS_DIR"] = path.join(tempDir, "empty-creds-store");

    // 1. Only generic/single app file exists -> _manifest MUST fail NotFound (or null on OsPlatform), never steal it!
    const appKey = crypto.randomBytes(32);
    const singleAppFile = path.join(credsDir, "systemd-provisioned-secret");
    fs.writeFileSync(singleAppFile, appKey.toString("base64"), "utf8");

    const manifestMissing = await keyring.get("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT);
    expect(manifestMissing.ok).toBe(false);
    if (!manifestMissing.ok) {
      expect(manifestMissing.category).toBe("NotFound");
    }
    const osManifestMissing = await osKeyring.get("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT);
    expect(osManifestMissing.ok && osManifestMissing.value).toBeNull();

    // 2. Replace with ONLY manifest credential file -> app lookup MUST ignore manifest file in single-file fallback!
    fs.unlinkSync(singleAppFile);
    const manifestKey = crypto.randomBytes(32);
    const manifestCredFile = path.join(credsDir, "nyssa-ai-configuration._manifest");
    fs.writeFileSync(manifestCredFile, manifestKey.toString("base64"), "utf8");

    const appMissing = await keyring.get("nyssa-ai-configuration", "OrdersService");
    expect(appMissing.ok).toBe(false);
    if (!appMissing.ok) {
      expect(appMissing.category).toBe("NotFound");
    }
    const osAppMissing = await osKeyring.get("nyssa-ai-configuration", "OrdersService");
    expect(osAppMissing.ok && osAppMissing.value).toBeNull();

    const manifestFound = await keyring.get("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT);
    expect(manifestFound.ok && manifestFound.value.equals(manifestKey)).toBe(true);
    const osManifestFound = await osKeyring.get("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT);
    expect(osManifestFound.ok && osManifestFound.value?.equals(manifestKey)).toBe(true);

    // 3. Coexistence: 1 manifest file + 1 single app file -> each resolves its own key cleanly!
    fs.writeFileSync(singleAppFile, appKey.toString("base64"), "utf8");
    const appCoexist = await keyring.get("nyssa-ai-configuration", "HeadlessWorker");
    expect(appCoexist.ok && appCoexist.value.equals(appKey)).toBe(true);
    const manifestCoexist = await keyring.get("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT);
    expect(manifestCoexist.ok && manifestCoexist.value.equals(manifestKey)).toBe(true);
  });

  it("keyringProtectedFile_manifestDirectoryIsolation_separatesAppAndManifestKeys", async () => {
    const appDir = path.join(tempDir, "app-dir");
    const manifestDir = path.join(tempDir, "manifest-dir");
    const keyring = new KeyringProtectedFile(appDir, manifestDir);

    const appKey = crypto.randomBytes(32);
    const manifestKey = crypto.randomBytes(32);

    expect((await keyring.set("nyssa-ai-configuration", "OrdersService", appKey)).ok).toBe(true);
    expect((await keyring.set("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT, manifestKey)).ok).toBe(true);

    const appKeyPath = path.join(appDir, KeyringProtectedFile.KEY_FILENAME);
    const manifestKeyPath = path.join(manifestDir, KeyringProtectedFile.MANIFEST_KEY_FILENAME);
    expect(fs.existsSync(appKeyPath)).toBe(true);
    expect(fs.existsSync(manifestKeyPath)).toBe(true);

    const readApp = await keyring.get("nyssa-ai-configuration", "OrdersService");
    expect(readApp.ok && readApp.value?.equals(appKey)).toBe(true);

    const readManifest = await keyring.get("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT);
    expect(readManifest.ok && readManifest.value?.equals(manifestKey)).toBe(true);

    // Deleting _manifest does not delete app .nyssa-key
    expect((await keyring.delete("nyssa-ai-configuration", KeyringDefault.MANIFEST_ACCOUNT)).ok).toBe(true);
    expect(fs.existsSync(manifestKeyPath)).toBe(false);
    expect(fs.existsSync(appKeyPath)).toBe(true);
  });

  it.skipIf(process.platform !== "linux")(
    "configurationBootstrap_resolveKeyringAndManifestKeyring_prefersExistingLinuxCredBeforeHeadlessFallback",
    () => {
      const storeCredsDir = path.join(tempDir, "systemd-store");
      fs.mkdirSync(storeCredsDir, { recursive: true });
      process.env["NYSSA_SYSTEMD_CREDS_DIR"] = storeCredsDir;

      const configDir = path.join(tempDir, "app-config");
      const manifestPath = path.join(tempDir, "manifest-dir", "config-manifest.json");
      fs.mkdirSync(configDir, { recursive: true });
      fs.mkdirSync(path.dirname(manifestPath), { recursive: true });

      // When .cred exists, resolveKeyring / resolveManifestKeyring selects KeyringOsPlatform even on headless Linux
      fs.writeFileSync(path.join(storeCredsDir, "nyssa-ai-configuration.LinuxWorker.cred"), "sealed", "utf8");
      fs.writeFileSync(path.join(storeCredsDir, "nyssa-ai-configuration._manifest.cred"), "sealed", "utf8");

      const appKeyring = ConfigurationBootstrap.resolveKeyring(configDir, {
        applicationName: "LinuxWorker",
        configManifest: manifestPath
      });
      expect(appKeyring).toBeInstanceOf(KeyringOsPlatform);

      const manifestKeyring = ConfigurationBootstrap.resolveManifestKeyring(manifestPath);
      expect(manifestKeyring).toBeInstanceOf(KeyringOsPlatform);

      // When .nyssa-key / .nyssa-manifest-key exists, ProtectedFile takes top priority over .cred
      fs.writeFileSync(path.join(configDir, KeyringProtectedFile.KEY_FILENAME), crypto.randomBytes(32).toString("base64"));
      fs.writeFileSync(
        path.join(path.dirname(manifestPath), KeyringProtectedFile.MANIFEST_KEY_FILENAME),
        crypto.randomBytes(32).toString("base64")
      );

      expect(
        ConfigurationBootstrap.resolveKeyring(configDir, {
          applicationName: "LinuxWorker",
          configManifest: manifestPath
        })
      ).toBeInstanceOf(KeyringProtectedFile);
      expect(ConfigurationBootstrap.resolveManifestKeyring(manifestPath)).toBeInstanceOf(KeyringProtectedFile);
    }
  );

  it("keyringSystemdCreds_getMissingFile_returnsNotFoundResult", async () => {
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const keyring = new KeyringSystemdCreds();

    const res = await keyring.get("nyssa-ai-configuration", "MissingApp");

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
      process.env["CREDENTIALS_DIRECTORY"] = credsDir;
      fs.writeFileSync(path.join(credsDir, "OrdersService.key"), invalidContent, "utf8");
      const keyring = new KeyringSystemdCreds();

      const res = await keyring.get("nyssa-ai-configuration", "OrdersService");

      expect(res.ok).toBe(false);
      if (!res.ok) {
        expect(res.category).toBe("Validation");
        expect(res.message.length).toBeGreaterThan(0);
      }
    }
  );

  it("keyringSystemdCreds_setAndDelete_returnUnsupportedError", async () => {
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const keyring = new KeyringSystemdCreds();
    const secret = crypto.randomBytes(32);

    const setRes = await keyring.set("nyssa-ai-configuration", "OrdersService", secret);
    const delRes = await keyring.delete("nyssa-ai-configuration", "OrdersService");

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
    process.env["CREDENTIALS_DIRECTORY"] = credsDir;
    const masterKey = crypto.randomBytes(32);
    fs.writeFileSync(
      path.join(credsDir, "custom-systemd-cred"),
      masterKey.toString("base64"),
      "utf8"
    );

    const manifestPath = path.join(tempDir, "config-manifest.json");
    const configDir = path.join(tempDir, "config");

    const bootRes = await ConfigurationBootstrap.createStore({
      applicationName: "SystemdServiceApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      systemdCredentialName: "custom-systemd-cred"
    });

    expect(bootRes.ok).toBe(true);
    if (!bootRes.ok) return;

    expect(bootRes.value.keyring).toBeInstanceOf(KeyringSystemdCreds);
    expect(fs.existsSync(path.join(configDir, KeyringProtectedFile.KEY_FILENAME))).toBe(false);

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
    const configDir = path.join(tempDir, "config-protected");
    fs.mkdirSync(configDir, { recursive: true });
    fs.writeFileSync(
      path.join(configDir, KeyringProtectedFile.KEY_FILENAME),
      crypto.randomBytes(32).toString("base64"),
      "utf8"
    );

    const keyring = ConfigurationBootstrap.resolveKeyring(configDir);

    expect(keyring).toBeInstanceOf(KeyringProtectedFile);
  });
});
