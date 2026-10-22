// Responsibility: Coordinate key generation and lookup with strict non-divergence custody policy.
// Public interface:
// +-- KeyringDefault
// |   +-- KEY_ACCOUNT
// |   +-- KEY_LENGTH
// |   `-- getOrCreateKey(applicationName, keyring, signal?)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringDefault coordinates key management.
 * Enforces .NET custody rule: a read error is never treated as an absent key.
 * Only explicit absence triggers generation of a new 32-byte key.
 */
export class KeyringDefault {
  public static readonly KEY_ACCOUNT = "configuration-key";
  public static readonly KEY_LENGTH = 32;

  /**
   * Retrieves or atomically creates a 32-byte master key.
   */
  public static async getOrCreateKey(
    applicationName: string,
    keyring: IKeyring,
    signal?: AbortSignal
  ): Promise<Result<Buffer>> {
    if (signal?.aborted) {
      return fail(`Operation aborted: ${signal.reason}`);
    }

    const readRes = await keyring.get(applicationName, this.KEY_ACCOUNT, signal);
    if (readRes.ok) {
      return readRes;
    }

    // Only create if the key is genuinely absent
    if (readRes.error.includes("does not exist") || readRes.error.includes("not found")) {
      const freshKey = crypto.randomBytes(this.KEY_LENGTH);
      const setRes = await keyring.set(applicationName, this.KEY_ACCOUNT, freshKey, signal);
      if (!setRes.ok) {
        return fail(`Failed to store new encryption key: ${setRes.error}`);
      }
      return ok(freshKey);
    }

    // A transient read failure or permission failure must fail rather than generate a new key
    return fail(`Failed to read encryption key for '${applicationName}': ${readRes.error}`);
  }
}
