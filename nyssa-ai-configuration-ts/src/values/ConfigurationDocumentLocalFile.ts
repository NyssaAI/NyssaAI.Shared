// Responsibility: Cross-process file locking and atomic modification of one namespace JSON document.
// Public interface:
// `-- ConfigurationDocumentLocalFile
//     +-- modify(modifier, signal)
//     +-- new(documentPath, lockTimeoutMs)
//     +-- read(signal)
//     `-- validateSegment(segment, kind)

import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import { CrossProcessFileLock } from "../fileSecurity/CrossProcessFileLock.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";

/**
 * ConfigurationDocumentLocalFile coordinates cross-process locking and atomic document mutations.
 * Supports .NET and Python .lock files using handle-based locking and retry backoff.
 */
export class ConfigurationDocumentLocalFile {
  private static readonly REGISTRY_NAMESPACE = "config-manifest";
  private static readonly WINDOWS_RESERVED_NAMES = new Set([
    "CON",
    "PRN",
    "AUX",
    "NUL",
    "COM1",
    "COM2",
    "COM3",
    "COM4",
    "COM5",
    "COM6",
    "COM7",
    "COM8",
    "COM9",
    "LPT1",
    "LPT2",
    "LPT3",
    "LPT4",
    "LPT5",
    "LPT6",
    "LPT7",
    "LPT8",
    "LPT9"
  ]);

  private readonly _documentPath: string;
  private readonly _lockTimeoutMs: number;
  private readonly _lock: CrossProcessFileLock;

  public constructor(documentPath: string, lockTimeoutMs: number = 10000) {
    this._documentPath = path.resolve(documentPath);
    this._lockTimeoutMs = lockTimeoutMs;
    this._lock = new CrossProcessFileLock(this._documentPath, `${this._documentPath}.lock`, lockTimeoutMs);
  }

  /**
   * Validates a namespace or key segment against path traversal and reserved names.
   */
  public static validateSegment(segment: string, kind: "namespace" | "key"): Result<void> {
    if (!segment || typeof segment !== "string" || segment.trim().length === 0) {
      return fail(`Configuration ${kind} cannot be blank.`, "Validation");
    }

    if (
      segment.includes("..") ||
      segment.includes("/") ||
      segment.includes("\\") ||
      segment === ".pending" ||
      /[\x00-\x1F\x7F]/.test(segment)
    ) {
      return fail(
        `Configuration ${kind} '${segment}' contains invalid path traversal or control characters.`,
        "Validation"
      );
    }

    if (kind === "namespace") {
      if (segment.startsWith(".")) {
        return fail(`Namespace '${segment}' must not start with '.'.`, "Validation");
      }
      if (!/^[A-Za-z0-9._-]+$/.test(segment)) {
        return fail(
          `Namespace '${segment}' must be letters, digits, '.', '_' or '-', not start with '.', and not name a reserved device.`,
          "Validation"
        );
      }
      const stem = (segment.split(".", 2)[0] ?? "").toUpperCase();
      if (this.WINDOWS_RESERVED_NAMES.has(stem)) {
        return fail(`Namespace '${segment}' names a reserved Windows device.`, "Validation");
      }
      const lower = segment.toLowerCase();
      if (
        lower === this.REGISTRY_NAMESPACE ||
        lower === `${this.REGISTRY_NAMESPACE}.json`
      ) {
        return fail(
          `Namespace '${segment}' is the configuration manifest, which a store may not write.`,
          "Validation"
        );
      }
    }

    return ok(undefined);
  }

  /**
   * Reads all configuration values in the document under the cross-process lock.
   */
  public async read(signal?: AbortSignal): Promise<Result<Record<string, ConfigurationValue>>> {
    signal?.throwIfAborted();
    if (this._lockTimeoutMs <= 0) {
      return fail(
        `configuration: the lock timeout must be positive; ${this._lockTimeoutMs}ms was supplied`,
        "Validation"
      );
    }

    const dirSafe = DirectorySafety.verify(path.dirname(this._documentPath));
    if (!dirSafe.ok) {
      return dirSafe;
    }

    return this._lock.withLock(async () => {
      signal?.throwIfAborted();
      if (!fs.existsSync(this._documentPath)) {
        return ok({});
      }

      const fileSafe = DirectorySafety.verifyFile(this._documentPath);
      if (!fileSafe.ok) {
        return fileSafe;
      }

      try {
        const raw = fs.readFileSync(this._documentPath, "utf8");
        if (!raw || raw.trim().length === 0) {
          return ok({});
        }
        const parsed = JSON.parse(raw) as unknown;
        if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
          return fail(
            `Reading '${this._documentPath}' failed validation: document root must be a JSON object.`,
            "Validation"
          );
        }
        return ok(parsed as Record<string, ConfigurationValue>);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        if (err instanceof SyntaxError) {
          return fail(`Reading '${this._documentPath}' failed validation: ${message}`, "Validation");
        }
        return fail(`Failed to read configuration document '${this._documentPath}': ${message}`, "Unexpected");
      }
    }, signal);
  }

  /**
   * Modifies entries in the configuration document atomically under the cross-process lock.
   */
  public async modify(
    modifier: (
      current: Record<string, ConfigurationValue>
    ) => Result<Record<string, ConfigurationValue>> | Promise<Result<Record<string, ConfigurationValue>>>,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    signal?.throwIfAborted();
    if (this._lockTimeoutMs <= 0) {
      return fail(
        `configuration: the lock timeout must be positive; ${this._lockTimeoutMs}ms was supplied`,
        "Validation"
      );
    }

    const defaultRegistryPath = path.resolve(
      os.homedir(),
      ".nyssa-ai",
      `${ConfigurationDocumentLocalFile.REGISTRY_NAMESPACE}.json`
    );
    if (
      process.platform === "win32"
        ? this._documentPath.toLowerCase() === defaultRegistryPath.toLowerCase()
        : this._documentPath === defaultRegistryPath
    ) {
      return fail(
        `Namespace '${path.basename(this._documentPath)}' is the configuration manifest, which a store may not write.`,
        "Validation"
      );
    }

    const dirSafe = DirectorySafety.verify(path.dirname(this._documentPath));
    if (!dirSafe.ok) {
      return dirSafe;
    }

    return this._lock.withLock(async () => {
      signal?.throwIfAborted();
      let current: Record<string, ConfigurationValue> = {};
      if (fs.existsSync(this._documentPath)) {
        const fileSafe = DirectorySafety.verifyFile(this._documentPath);
        if (!fileSafe.ok) {
          return fileSafe;
        }
        try {
          const raw = fs.readFileSync(this._documentPath, "utf8");
          if (raw && raw.trim().length > 0) {
            const parsed = JSON.parse(raw) as unknown;
            if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
              return fail(
                `Reading '${this._documentPath}' failed validation: document root must be a JSON object.`,
                "Validation"
              );
            }
            current = parsed as Record<string, ConfigurationValue>;
          }
        } catch (err) {
          const message = err instanceof Error ? err.message : String(err);
          if (err instanceof SyntaxError) {
            return fail(`Reading '${this._documentPath}' failed validation: ${message}`, "Validation");
          }
          return fail(`Failed to read existing configuration for modification: ${message}`, "Unexpected");
        }
      }

      const modResult = await modifier({ ...current });
      signal?.throwIfAborted();
      if (!modResult.ok) {
        return modResult;
      }

      const updated = modResult.value;
      const json = JSON.stringify(updated, null, 2);

      const tempPath = `${this._documentPath}.${crypto.randomUUID()}.tmp`;
      try {
        const createRes = ProtectedFileSystem.createFile(tempPath, json);
        if (!createRes.ok) {
          return createRes;
        }
        fs.renameSync(tempPath, this._documentPath);
        return ok(undefined);
      } catch (err) {
        try {
          if (fs.existsSync(tempPath)) fs.unlinkSync(tempPath);
        } catch {
          // ignore
        }
        const message = err instanceof Error ? err.message : String(err);
        return fail(`Failed to write updated configuration: ${message}`, "Unexpected");
      }
    }, signal);
  }
}
