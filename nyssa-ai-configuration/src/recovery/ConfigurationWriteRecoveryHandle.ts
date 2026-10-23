// Responsibility: Portable recovery token allowing an interrupted multi-step write to be resumed.
// Public interface:
// `-- ConfigurationWriteRecoveryHandle
//     +-- export()
//     +-- import(token)
//     +-- namespace
//     +-- new(namespace, operationId, journalPath)
//     `-- operationId

import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";

/**
 * ConfigurationWriteRecoveryHandle identifies a pending write-ahead journal for a namespace
 * and supports base64url export/import across process restarts.
 */
export class ConfigurationWriteRecoveryHandle {
  private static readonly VERSION = 1;
  private static readonly NAMESPACE_REGEX = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;

  public readonly namespace: string;
  public readonly operationId: string;
  private readonly _journalPath: string | undefined;

  public constructor(namespace: string, operationId: string, journalPath?: string) {
    this.namespace = namespace;
    this.operationId = operationId;
    this._journalPath = journalPath;
  }

  /**
   * Serializes the recovery handle into a portable base64url token.
   */
  public export(): string {
    const payload = JSON.stringify({
      version: ConfigurationWriteRecoveryHandle.VERSION,
      namespace: this.namespace,
      operationId: this.operationId,
      ...(this._journalPath ? { journalPath: path.basename(this._journalPath) } : {})
    });
    return Buffer.from(payload, "utf8").toString("base64url");
  }

  /**
   * Parses and validates a base64url recovery token.
   */
  public static import(token: string): Result<ConfigurationWriteRecoveryHandle> {
    if (!token || typeof token !== "string" || token.trim().length === 0) {
      return fail("Recovery handle token cannot be blank.", "Validation");
    }

    try {
      const json = Buffer.from(token.trim(), "base64url").toString("utf8");
      const parsed = JSON.parse(json) as {
        version?: unknown;
        namespace?: unknown;
        operationId?: unknown;
        journalPath?: unknown;
      };

      if (parsed.version !== ConfigurationWriteRecoveryHandle.VERSION) {
        return fail(
          `Unsupported recovery handle version '${String(parsed.version)}'; expected version ${ConfigurationWriteRecoveryHandle.VERSION}.`,
          "Validation"
        );
      }

      if (
        typeof parsed.namespace !== "string" ||
        !ConfigurationWriteRecoveryHandle.NAMESPACE_REGEX.test(parsed.namespace) ||
        parsed.namespace.includes("..")
      ) {
        return fail(`Invalid namespace in recovery handle: '${String(parsed.namespace)}'.`, "Validation");
      }

      if (typeof parsed.operationId !== "string" || parsed.operationId.trim().length === 0) {
        return fail("Recovery handle is missing a valid operationId.", "Validation");
      }

      if (parsed.journalPath !== undefined) {
        if (
          typeof parsed.journalPath !== "string" ||
          parsed.journalPath.includes("..") ||
          parsed.journalPath.includes("/") ||
          parsed.journalPath.includes("\\")
        ) {
          return fail("Recovery handle contains an invalid journalPath.", "Validation");
        }
      }

      return ok(
        new ConfigurationWriteRecoveryHandle(
          parsed.namespace,
          parsed.operationId,
          typeof parsed.journalPath === "string" ? parsed.journalPath : undefined
        )
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Malformed recovery handle token: ${message}`, "Validation");
    }
  }
}
