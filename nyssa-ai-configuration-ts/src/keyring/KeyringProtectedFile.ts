// Responsibility: Store the 32-byte master key in a protected file (.nyssa-key or .nyssa-manifest-key) for headless and container environments.
// Public interface:
// `-- KeyringProtectedFile
//     +-- KEY_FILENAME
//     +-- MANIFEST_KEY_FILENAME
//     +-- delete(service, account, signal)
//     +-- get(service, account, signal)
//     +-- new(configurationDirectory, manifestDirectory)
//     `-- set(service, account, secret, signal)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import { KeyringDefault } from "./KeyringDefault.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringProtectedFile stores the master key encoded as Base64 in a protected 0600 file.
 * Used for headless CI and Docker environments where OS D-Bus or desktop keychains are absent.
 */
export class KeyringProtectedFile {
  public static readonly KEY_FILENAME = ".nyssa-key";
  public static readonly MANIFEST_KEY_FILENAME = ".nyssa-manifest-key";

  private readonly _configurationDirectory: string;
  private readonly _manifestDirectory: string;

  public constructor(configurationDirectory: string, manifestDirectory?: string) {
    this._configurationDirectory = configurationDirectory;
    this._manifestDirectory = manifestDirectory ?? configurationDirectory;
  }

  public async get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer | null>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("Keyring service and account must be non-empty.", "Validation");
    }

    const keyFilePath = this.resolveKeyFilePath(account);
    if (!fs.existsSync(keyFilePath)) {
      return ok(null);
    }

    const safetyRes = DirectorySafety.verifyPrivateFile(keyFilePath);
    if (!safetyRes.ok) {
      return fail(`Insecure key file rejected: ${safetyRes.error}`, safetyRes.category ?? "Forbidden");
    }

    try {
      const b64 = fs.readFileSync(keyFilePath, "utf8").trim();
      if (!/^[A-Za-z0-9+/]+=*$/.test(b64)) {
        return fail(
          `keyring: '${service}/${account}' does not hold 32 bytes of base64 material`,
          "Validation"
        );
      }
      const keyBuf = Buffer.from(b64, "base64");
      if (keyBuf.length !== 32) {
        return fail(
          `Stored key in '${keyFilePath}' is ${keyBuf.length} bytes; expected 32.`,
          "Validation"
        );
      }
      return ok(keyBuf);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to read key file '${keyFilePath}': ${message}`, "Unavailable");
    }
  }

  public async set(service: string, account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("Keyring service and account must be non-empty.", "Validation");
    }

    if (secret.length !== 32) {
      return fail(`Key must be exactly 32 bytes; ${secret.length} bytes supplied.`, "Validation");
    }

    const keyFilePath = this.resolveKeyFilePath(account);
    try {
      const b64 = secret.toString("base64");
      const writeRes = ProtectedFileSystem.createFile(keyFilePath, b64);
      if (!writeRes.ok) {
        return writeRes;
      }
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to write key file '${keyFilePath}': ${message}`, "Unavailable");
    }
  }

  public async delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("Keyring service and account must be non-empty.", "Validation");
    }

    const keyFilePath = this.resolveKeyFilePath(account);
    try {
      if (fs.existsSync(keyFilePath)) {
        fs.unlinkSync(keyFilePath);
      }
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to delete key file '${keyFilePath}': ${message}`, "Unavailable");
    }
  }

  private resolveKeyFilePath(account: string): string {
    if (account.trim() === KeyringDefault.MANIFEST_ACCOUNT) {
      return path.join(this._manifestDirectory, KeyringProtectedFile.MANIFEST_KEY_FILENAME);
    }
    return path.join(this._configurationDirectory, KeyringProtectedFile.KEY_FILENAME);
  }
}

const _typeCheck: IKeyring = null as unknown as KeyringProtectedFile;
void _typeCheck;
