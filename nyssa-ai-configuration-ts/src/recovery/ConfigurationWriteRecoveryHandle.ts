// Responsibility: Backward-compatible alias for ConfigurationRecoveryHandle.
// Public interface:
// `-- ConfigurationWriteRecoveryHandle
//     +-- export()
//     +-- import(token)
//     +-- importToken(token)
//     +-- new(token)
//     `-- toString()

import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationRecoveryHandle } from "./ConfigurationRecoveryHandle.js";

/**
 * @deprecated Prefer `ConfigurationRecoveryHandle`.
 */
export class ConfigurationWriteRecoveryHandle {
  private readonly _token: string;

  public constructor(token: string) {
    this._token = token;
  }

  public static import(token: string): Result<ConfigurationRecoveryHandle> {
    if (!token || typeof token !== "string" || token.trim().length === 0) {
      return fail("Recovery handle token cannot be blank.", "Validation");
    }
    return ok(new ConfigurationRecoveryHandle(token.trim()));
  }

  public static importToken(token: string): Result<ConfigurationRecoveryHandle> {
    return this.import(token);
  }

  public export(): string {
    return this._token;
  }

  public toString(): string {
    return "ConfigurationRecoveryHandle { protected }";
  }
}
