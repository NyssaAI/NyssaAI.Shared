// Responsibility: Seal and open configuration values using AES-256-GCM matching .NET ConfigurationCipherAesGcm.
// Public interface:
// +-- ConfigurationCipherAesGcm
// |   +-- create(key)
// |   +-- open(value, namespace, key)
// |   `-- seal(value, namespace, key)

import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import type { IConfigurationCipher } from "./IConfigurationCipher.js";

/**
 * ConfigurationCipherAesGcm seals values with AES-256-GCM (Format Version 2).
 * Nonce: 12 bytes, Auth Tag: 16 bytes.
 * AAD: [0x02, int32BE(nsLen), nsBytes, int32BE(keyLen), keyBytes].
 * Serialized output: Base64URL( [0x02, nonce(12), tag(16), ciphertext] ).
 */
export class ConfigurationCipherAesGcm implements IConfigurationCipher {
  public static readonly FORMAT_VERSION = 2;
  public static readonly NONCE_LENGTH = 12;
  public static readonly TAG_LENGTH = 16;
  public static readonly KEY_LENGTH = 32;
  public static readonly HEADER_LENGTH = 1 + ConfigurationCipherAesGcm.NONCE_LENGTH + ConfigurationCipherAesGcm.TAG_LENGTH;

  private readonly _key: Buffer;

  private constructor(key: Buffer) {
    this._key = Buffer.from(key);
  }

  /**
   * Creates a cipher with a 32-byte key.
   */
  public static create(key: Buffer | Uint8Array): Result<ConfigurationCipherAesGcm> {
    if (!key || key.length !== this.KEY_LENGTH) {
      return fail(`AES-256 requires a 32-byte key; ${key ? key.length : 0} bytes supplied.`);
    }

    return ok(new ConfigurationCipherAesGcm(Buffer.from(key)));
  }

  /**
   * Builds the length-prefixed big-endian AAD buffer.
   */
  public static buildAssociatedData(namespace: string, key: string): Buffer {
    const nsBytes = Buffer.from(namespace, "utf8");
    const keyBytes = Buffer.from(key, "utf8");

    const totalLen = 1 + 4 + nsBytes.length + 4 + keyBytes.length;
    const buf = Buffer.alloc(totalLen);

    buf[0] = ConfigurationCipherAesGcm.FORMAT_VERSION;
    buf.writeInt32BE(nsBytes.length, 1);
    nsBytes.copy(buf, 5);

    const keyOffset = 5 + nsBytes.length;
    buf.writeInt32BE(keyBytes.length, keyOffset);
    keyBytes.copy(buf, keyOffset + 4);

    return buf;
  }

  public seal(value: string, namespace: string, key: string): Result<string> {
    try {
      const nonce = crypto.randomBytes(ConfigurationCipherAesGcm.NONCE_LENGTH);
      const plaintext = Buffer.from(value, "utf8");
      const aad = ConfigurationCipherAesGcm.buildAssociatedData(namespace, key);

      const cipher = crypto.createCipheriv("aes-256-gcm", this._key, nonce);
      cipher.setAAD(aad);

      const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
      const tag = cipher.getAuthTag();

      const payload = Buffer.concat([
        Buffer.from([ConfigurationCipherAesGcm.FORMAT_VERSION]),
        nonce,
        tag,
        ciphertext
      ]);

      const base64url = payload
        .toString("base64")
        .replace(/=/g, "")
        .replace(/\+/g, "-")
        .replace(/\//g, "_");

      return ok(base64url);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to seal configuration value: ${message}`);
    }
  }

  public open(value: string, namespace: string, key: string): Result<string> {
    try {
      let base64 = value.replace(/-/g, "+").replace(/_/g, "/");
      while (base64.length % 4 !== 0) {
        base64 += "=";
      }

      const payload = Buffer.from(base64, "base64");
      if (
        payload.length < ConfigurationCipherAesGcm.HEADER_LENGTH ||
        payload[0] !== ConfigurationCipherAesGcm.FORMAT_VERSION
      ) {
        return fail("The sealed value is not a well-formed version 2 AES-256-GCM payload.");
      }

      const nonce = payload.subarray(1, 1 + ConfigurationCipherAesGcm.NONCE_LENGTH);
      const tag = payload.subarray(
        1 + ConfigurationCipherAesGcm.NONCE_LENGTH,
        ConfigurationCipherAesGcm.HEADER_LENGTH
      );
      const ciphertext = payload.subarray(ConfigurationCipherAesGcm.HEADER_LENGTH);
      const aad = ConfigurationCipherAesGcm.buildAssociatedData(namespace, key);

      const decipher = crypto.createDecipheriv("aes-256-gcm", this._key, nonce);
      decipher.setAAD(aad);
      decipher.setAuthTag(tag);

      const plaintext = Buffer.concat([decipher.update(ciphertext), decipher.final()]);
      return ok(plaintext.toString("utf8"));
    } catch {
      return fail(
        "The sealed value failed authentication: it was altered, or sealed against a different namespace and key."
      );
    }
  }
}
