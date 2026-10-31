// Responsibility: OS keychain adapter supporting Windows Credential Manager, macOS Keychain, and Linux Secret Service.
// Public interface:
// `-- KeyringOsPlatform
//     +-- delete(service, account, signal)
//     +-- get(service, account, signal)
//     `-- set(service, account, secret, signal)

import { execFileSync } from "node:child_process";
import { ok, fail, type Result } from "../results/Result.js";
import { KeyringWindowsCredMan } from "./KeyringWindowsCredMan.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringOsPlatform interacts with the native OS credential store using identical
 * service/account addressing and base64 encoding as .NET KeyringWindows, KeyringMacOs, and KeyringLinux.
 */
export class KeyringOsPlatform {
  public async get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer | null>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    let rawRes: Result<string | null>;
    if (process.platform === "win32") {
      rawRes = KeyringWindowsCredMan.get(addrRes.value);
    } else if (process.platform === "darwin") {
      rawRes = this.getMacOs(service, account);
    } else if (process.platform === "linux") {
      rawRes = this.getLinux(service, account);
    } else {
      return fail(`Unsupported platform '${process.platform}' for OS keyring.`, "Unavailable");
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
    if (process.platform === "win32") {
      return KeyringWindowsCredMan.set(addrRes.value, encoded);
    }
    if (process.platform === "darwin") {
      return this.setMacOs(service, account, encoded);
    }
    if (process.platform === "linux") {
      return this.setLinux(service, account, addrRes.value, encoded);
    }
    return fail(`Unsupported platform '${process.platform}' for OS keyring.`, "Unavailable");
  }

  public async delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const addrRes = this.validateAddress(service, account);
    if (!addrRes.ok) {
      return addrRes;
    }

    if (process.platform === "win32") {
      return KeyringWindowsCredMan.delete(addrRes.value);
    }
    if (process.platform === "darwin") {
      return this.deleteMacOs(service, account);
    }
    if (process.platform === "linux") {
      return this.deleteLinux(service, account);
    }
    return fail(`Unsupported platform '${process.platform}' for OS keyring.`, "Unavailable");
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
      const output = execFileSync(
        "/usr/bin/security",
        ["find-generic-password", "-s", service, "-a", account, "-w"],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
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
      execFileSync(
        "/usr/bin/security",
        ["add-generic-password", "-U", "-s", service, "-a", account, "-w", secret],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: SecItemAdd failed: ${message}`, "Unavailable");
    }
  }

  private deleteMacOs(service: string, account: string): Result<void> {
    try {
      execFileSync(
        "/usr/bin/security",
        ["delete-generic-password", "-s", service, "-a", account],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
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

  private getLinux(service: string, account: string): Result<string | null> {
    try {
      const output = execFileSync(
        "secret-tool",
        ["lookup", "service", service, "username", account],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(output.trim());
    } catch (err: unknown) {
      const status = (err as { status?: number; stderr?: Buffer | string }).status;
      const stderr = String((err as { stderr?: Buffer | string }).stderr ?? "").trim();
      if (status === 1 && stderr.length === 0) {
        return ok(null);
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: the Secret Service reported error: ${stderr || message}`, "Unavailable");
    }
  }

  private setLinux(service: string, account: string, label: string, secret: string): Result<void> {
    try {
      execFileSync(
        "secret-tool",
        ["store", `--label=${label}`, "service", service, "username", account],
        { input: secret, encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: the Secret Service refused write: ${message}`, "Unavailable");
    }
  }

  private deleteLinux(service: string, account: string): Result<void> {
    try {
      execFileSync(
        "secret-tool",
        ["clear", "service", service, "username", account],
        { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }
      );
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`keyring: the Secret Service refused deletion: ${message}`, "Unavailable");
    }
  }
}

const _typeCheck: IKeyring = null as unknown as KeyringOsPlatform;
void _typeCheck;
