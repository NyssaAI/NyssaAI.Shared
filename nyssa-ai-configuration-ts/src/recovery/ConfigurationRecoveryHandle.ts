// Responsibility: Represent an opaque, authenticated recovery token with redacted string formatting.
// Public interface:
// `-- ConfigurationRecoveryHandle
//     +-- export()
//     +-- import(token)
//     +-- importToken(token)
//     +-- new(token)
//     `-- toString()

import { ok, fail, type Result } from "../results/Result.js";

/**
 * ConfigurationRecoveryHandle wraps an opaque, AEAD-sealed recovery token persisted only via explicit export().
 * Import does not authenticate the token; recover() authenticates it against the application's HKDF-SHA256 subkey
 * and canonical configuration root before contacting any provider.
 */
export class ConfigurationRecoveryHandle {
  private readonly _token: string;

  public constructor(token: string) {
    this._token = token;
  }

  /**
   * Imports a previously exported token string for validation during recover().
   */
  public static import(token: string): Result<ConfigurationRecoveryHandle> {
    if (!token || typeof token !== "string" || token.trim().length === 0) {
      return fail("Recovery handle token cannot be blank.", "Validation");
    }
    return ok(new ConfigurationRecoveryHandle(token.trim()));
  }

  /**
   * Alias for import(token) matching Python's import_token naming.
   */
  public static importToken(token: string): Result<ConfigurationRecoveryHandle> {
    return this.import(token);
  }

  /**
   * Exports the protected token string for caller-owned persistence. Treat as sensitive and never log.
   */
  public export(): string {
    return this._token;
  }

  /**
   * Returns a redacted description so the raw token is never leaked in logs.
   */
  public toString(): string {
    return "ConfigurationRecoveryHandle { protected }";
  }
}
