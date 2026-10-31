// Responsibility: Coordinate key generation and lookup with strict non-divergence custody policy.
// Public interface:
// `-- KeyringDefault
//     +-- KEY_ACCOUNT
//     +-- KEY_LENGTH
//     +-- SERVICE_NAME
//     +-- getKey(applicationName, keyring, signal)
//     `-- getOrCreateKey(applicationName, keyring, signal)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringDefault coordinates key management.
 * Enforces .NET custody rule: a read error is never treated as an absent key.
 * Only explicit absence (ok(null)) triggers generation of a new 32-byte key.
 */
export class KeyringDefault {
  public static readonly SERVICE_NAME = "nyssa-ai-configuration";
  public static readonly KEY_ACCOUNT = "configuration-key";
  public static readonly KEY_LENGTH = 32;

  /**
   * Retrieves an existing 32-byte master key without creating one.
   */
  public static async getKey(
    applicationName: string,
    keyring: IKeyring,
    signal?: AbortSignal
  ): Promise<Result<Buffer | null>> {
    signal?.throwIfAborted();

    const readRes = await keyring.get(this.SERVICE_NAME, applicationName, signal);
    signal?.throwIfAborted();
    if (!readRes.ok) {
      return fail(
        `Failed to read encryption key for '${applicationName}': ${readRes.error}`,
        readRes.category ?? "Unavailable"
      );
    }

    if (readRes.value === null) {
      return ok(null);
    }

    if (readRes.value.length !== this.KEY_LENGTH) {
      return fail(
        `keyring: '${this.SERVICE_NAME}/${applicationName}' does not hold ${this.KEY_LENGTH} bytes of key material`,
        "Validation"
      );
    }

    return ok(Buffer.from(readRes.value));
  }

  /**
   * Retrieves or atomically creates a 32-byte master key.
   */
  public static async getOrCreateKey(
    applicationName: string,
    keyring: IKeyring,
    signal?: AbortSignal
  ): Promise<Result<Buffer>> {
    signal?.throwIfAborted();

    const existingRes = await this.getKey(applicationName, keyring, signal);
    if (!existingRes.ok) {
      return existingRes;
    }

    if (existingRes.value !== null) {
      return ok(existingRes.value);
    }

    const freshKey = crypto.randomBytes(this.KEY_LENGTH);
    const setRes = await keyring.set(this.SERVICE_NAME, applicationName, freshKey, signal);
    signal?.throwIfAborted();
    if (!setRes.ok) {
      freshKey.fill(0);
      return fail(
        `Failed to store new encryption key: ${setRes.error}`,
        setRes.category ?? "Unavailable"
      );
    }

    return ok(freshKey);
  }
}
