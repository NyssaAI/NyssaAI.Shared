// Responsibility: OS keychain adapter supporting Windows Credential Manager, macOS Keychain, and Linux systemd-creds.
// Public interface:
// `-- KeyringOsPlatform
//     +-- delete(service, account, signal)
//     +-- get(service, account, signal)
//     +-- hasLinuxCredentialFile(service, account)
//     +-- new(options)
//     `-- set(service, account, secret, signal)

import { execFileSync } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { KeyringWindowsCredMan } from "./KeyringWindowsCredMan.js";
import { KeyringSystemdCreds } from "./KeyringSystemdCreds.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringOsPlatform interacts with the native OS credential store using identical
 * service/account addressing and base64 encoding as .NET KeyringWindows, KeyringMacOs, and KeyringLinux (systemd-creds).
 */
export class KeyringOsPlatform {
  private readonly _platform: NodeJS.Platform;
  private readonly _commandRunner: (command: string, args: readonly string[], input?: string) => string;

  public constructor(options?: {
    readonly platform?: NodeJS.Platform | undefined;
    readonly commandRunner?:
      | ((command: string, args: readonly string[], input?: string) => string)
      | undefined;
  }) {
    this._platform = options?.platform ?? process.platform;
    this._commandRunner =
      options?.commandRunner ??
      ((command, args, input) =>
        execFileSync(command, args, {
          ...(input !== undefined ? { input } : {}),
          encoding: "utf8",
          stdio: input !== undefined ? ["pipe", "pipe", "pipe"] : ["ignore", "pipe", "pipe"]
        }));
  }

  public static hasLinuxCredentialFile(service: string, account: string): boolean {
    try {
      const { credFilePath } = this.resolveLinuxCredentialPaths(service, account);
      return fs.existsSync(credFilePath) && fs.statSync(credFilePath).isFile();
    } catch {
      return false;
    }
  }

  public async get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer | null>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    let rawRes: Result<string | null>;
    if (this._platform === "win32") {
      rawRes = KeyringWindowsCredMan.get(addrRes.value);
    } else if (this._platform === "darwin") {
      rawRes = this.getMacOs(service, account);
    } else if (this._platform === "linux") {
      rawRes = this.getLinux(service, account);
    } else {
      return fail(`Unsupported platform '${this._platform}' for OS keyring.`, "Unavailable");
    }

    if (!rawRes.ok) {
      return rawRes;
    }
    if (rawRes.value === null) {
      return ok(null);
    }

    const b64 = rawRes.value.trim();
    if (!/^[A-Za-z0-9+/]+=*$/.test(b64)) {
      return fail(
        `keyring: '${service}/${account}' does not hold 32 bytes of base64 material`,
        "Validation"
      );
    }

    const decoded = Buffer.from(b64, "base64");
    if (decoded.length !== 32) {
      return fail(
        `keyring: '${service}/${account}' does not hold 32 bytes of base64 material`,
        "Validation"
      );
    }

    return ok(decoded);
  }

  public async set(service: string, account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    if (secret.length < 16 || secret.length > 64) {
      return fail(
        `keyring: ${secret.length} bytes is outside the supported range of 16 to 64`,
        "Validation"
      );
    }

    const encoded = secret.toString("base64");
    if (this._platform === "win32") {
      return KeyringWindowsCredMan.set(addrRes.value, encoded);
    }
    if (this._platform === "darwin") {
      return this.setMacOs(service, account, encoded);
    }
    if (this._platform === "linux") {
      return this.setLinux(service, account, addrRes.value, encoded);
    }
    return fail(`Unsupported platform '${this._platform}' for OS keyring.`, "Unavailable");
  }

  public async delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    if (this._platform === "win32") {
      return KeyringWindowsCredMan.delete(addrRes.value);
    }
    if (this._platform === "darwin") {
      return this.deleteMacOs(service, account);
    }
    if (this._platform === "linux") {
      return this.deleteLinux(service, account);
    }
    return fail(`Unsupported platform '${this._platform}' for OS keyring.`, "Unavailable");
  }

  private validateAddress(service: string, account: string): Result<string> {
    if (!service || service.trim().length === 0) {
      return fail("keyring: service name must not be blank", "Validation");
    }
    if (!account || account.trim().length === 0) {
      return fail("keyring: key name must not be blank", "Validation");
    }
    if (/[\x00-\x1F\x7F]/.test(service) || /[\x00-\x1F\x7F]/.test(account)) {
      return fail("keyring: service and key must not contain control characters", "Validation");
    }
    return ok(`${service}/${account}`);
  }

  private getMacOs(service: string, account: string): Result<string | null> {
    try {
      const output = this._commandRunner("/usr/bin/security", [
        "find-generic-password",
        "-s",
        service,
        "-a",
        account,
        "-w"
      ]);
      return ok(output.trim());
    } catch (err: unknown) {
      const status = (err as { status?: number }).status;
      if (status === 44) {
        return ok(null);
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemCopyMatching failed: ${message}`, "Unavailable");
    }
  }

  private setMacOs(service: string, account: string, secret: string): Result<void> {
    try {
      this._commandRunner("/usr/bin/security", [
        "add-generic-password",
        "-U",
        "-s",
        service,
        "-a",
        account,
        "-w",
        secret
      ]);
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemAdd failed: ${message}`, "Unavailable");
    }
  }

  private deleteMacOs(service: string, account: string): Result<void> {
    try {
      this._commandRunner("/usr/bin/security", [
        "delete-generic-password",
        "-s",
        service,
        "-a",
        account
      ]);
      return ok(undefined);
    } catch (err: unknown) {
      const status = (err as { status?: number }).status;
      if (status === 44) {
        return ok(undefined);
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemDelete failed: ${message}`, "Unavailable");
    }
  }

  private static sanitizeSegment(segment: string): string {
    return KeyringSystemdCreds.sanitizeSegment(segment);
  }

  private static resolveLinuxCredentialPaths(service: string, account: string): {
    readonly credName: string;
    readonly credsStoreDir: string;
    readonly credFilePath: string;
  } {
    const credName = `${this.sanitizeSegment(service)}.${this.sanitizeSegment(account)}`;
    const envDir = process.env["NYSSA_SYSTEMD_CREDS_DIR"]?.trim();
    const credsStoreDir =
      envDir && envDir.length > 0 ? path.resolve(envDir) : path.join(os.homedir(), ".nyssa-ai", "credentials");
    const credFilePath = path.join(credsStoreDir, `${credName}.cred`);
    return { credName, credsStoreDir, credFilePath };
  }

  private static systemdUserArgs(): readonly string[] {
    return typeof process.geteuid === "function" && process.geteuid() === 0 ? [] : ["--user"];
  }

  private getLinux(service: string, account: string): Result<string | null> {
    const { credName, credFilePath } = KeyringOsPlatform.resolveLinuxCredentialPaths(service, account);

    if (KeyringSystemdCreds.isAvailable()) {
      const credentialsDir = path.resolve(process.env["CREDENTIALS_DIRECTORY"]!.trim());
      const found = KeyringSystemdCreds.findCredentialFile(credentialsDir, service.trim(), account.trim());
      if (found.ok && found.value !== null) {
        try {
          const rawBytes = fs.readFileSync(found.value);
          return ok(rawBytes.length === 32 ? rawBytes.toString("base64") : rawBytes.toString("utf8").trim());
        } catch {
          // fall through to credFilePath
        }
      }
    }

    if (!fs.existsSync(credFilePath)) {
      return ok(null);
    }

    try {
      const output = this._commandRunner("systemd-creds", [
        ...KeyringOsPlatform.systemdUserArgs(),
        "decrypt",
        `--name=${credName}`,
        credFilePath,
        "-"
      ]);
      return ok(output.trim());
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: systemd-creds decrypt failed: ${message}`, "Unavailable");
    }
  }

  private setLinux(service: string, account: string, _label: string, secret: string): Result<void> {
    const { credName, credsStoreDir, credFilePath } = KeyringOsPlatform.resolveLinuxCredentialPaths(service, account);
    const tempFilePath = `${credFilePath}.${process.pid}.tmp`;

    try {
      fs.mkdirSync(credsStoreDir, { recursive: true, mode: 0o700 });
      this._commandRunner(
        "systemd-creds",
        [
          ...KeyringOsPlatform.systemdUserArgs(),
          "encrypt",
          `--name=${credName}`,
          "-",
          tempFilePath
        ],
        secret
      );
      if (process.platform !== "win32") {
        fs.chmodSync(tempFilePath, 0o600);
      }
      fs.renameSync(tempFilePath, credFilePath);
      return ok(undefined);
    } catch (err) {
      try {
        if (fs.existsSync(tempFilePath)) {
          fs.unlinkSync(tempFilePath);
        }
      } catch {
        // ignore cleanup failure
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: systemd-creds encrypt failed: ${message}`, "Unavailable");
    }
  }

  private deleteLinux(service: string, account: string): Result<void> {
    const { credFilePath } = KeyringOsPlatform.resolveLinuxCredentialPaths(service, account);
    try {
      if (fs.existsSync(credFilePath)) {
        fs.unlinkSync(credFilePath);
      }
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: systemd-creds delete failed: ${message}`, "Unavailable");
    }
  }
}

const _typeCheck: IKeyring = null as unknown as KeyringOsPlatform;
void _typeCheck;
