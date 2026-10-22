// Responsibility: Store the 32-byte master key in a protected file (.nyssa-key) for headless and container environments.
// Public interface:
// +-- KeyringProtectedFile
// |   +-- KEY_FILENAME
// |   +-- get(service, account, signal?)
// |   `-- set(service, account, secret, signal?)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringProtectedFile stores the master key encoded as Base64 in a protected 0600 file.
 * Used for headless CI and Docker environments where OS D-Bus or desktop keychains are absent.
 */
export class KeyringProtectedFile implements IKeyring {
  public static readonly KEY_FILENAME = ".nyssa-key";

  private readonly _keyFilePath: string;

  public constructor(configurationDirectory: string) {
    this._keyFilePath = path.join(configurationDirectory, KeyringProtectedFile.KEY_FILENAME);
  }

  public async get(_service: string, _account: string, signal?: AbortSignal): Promise<Result<Buffer>> {
    if (signal?.aborted) {
      return fail(`Operation aborted: ${signal.reason}`);
    }

    if (!fs.existsSync(this._keyFilePath)) {
      return fail(`Key file does not exist: '${this._keyFilePath}'`);
    }

    const safetyRes = DirectorySafety.verifyPrivateFile(this._keyFilePath);
    if (!safetyRes.ok) {
      return fail(`Insecure key file rejected: ${safetyRes.error}`);
    }

    try {
      const b64 = fs.readFileSync(this._keyFilePath, "utf8").trim();
      const keyBuf = Buffer.from(b64, "base64");
      if (keyBuf.length !== 32) {
        return fail(`Stored key in '${this._keyFilePath}' is ${keyBuf.length} bytes; expected 32.`);
      }
      return ok(keyBuf);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to read key file '${this._keyFilePath}': ${message}`);
    }
  }

  public async set(_service: string, _account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>> {
    if (signal?.aborted) {
      return fail(`Operation aborted: ${signal.reason}`);
    }

    if (secret.length !== 32) {
      return fail(`Key must be exactly 32 bytes; ${secret.length} bytes supplied.`);
    }

    try {
      const b64 = secret.toString("base64");
      const writeRes = ProtectedFileSystem.createFile(this._keyFilePath, b64);
      if (!writeRes.ok) {
        return fail(writeRes.error);
      }
      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to write key file '${this._keyFilePath}': ${message}`);
    }
  }
}
