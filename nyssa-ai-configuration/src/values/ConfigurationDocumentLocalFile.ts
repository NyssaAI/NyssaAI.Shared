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
import type { ConfigurationValue } from "./ConfigurationValue.js";

/**
 * ConfigurationDocumentLocalFile coordinates cross-process locking and atomic document mutations.
 * Supports .NET .lock files left on disk using handle-based locking and retry backoff.
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
  private static readonly inProcessQueues = new Map<string, Promise<unknown>>();

  private readonly _documentPath: string;
  private readonly _lockPath: string;
  private readonly _lockTimeoutMs: number;

  public constructor(documentPath: string, lockTimeoutMs: number = 10000) {
    this._documentPath = path.resolve(documentPath);
    this._lockPath = `${this._documentPath}.lock`;
    this._lockTimeoutMs = lockTimeoutMs;
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

    return this.withLock(async () => {
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

    return this.withLock(async () => {
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

      // Unique sibling temp file to prevent write collisions
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

  /**
   * Acquires the in-process and cross-process lock and executes the action.
   */
  private async withLock<T>(action: () => Promise<Result<T>>, signal?: AbortSignal): Promise<Result<T>> {
    signal?.throwIfAborted();
    const lockKey = process.platform === "win32" ? this._lockPath.toLowerCase() : this._lockPath;
    const start = Date.now();

    const prev = ConfigurationDocumentLocalFile.inProcessQueues.get(lockKey) ?? Promise.resolve();
    let releaseQueue!: () => void;
    const currentGate = new Promise<void>((resolve) => {
      releaseQueue = resolve;
    });
    const nextInQueue = prev.then(() => currentGate, () => currentGate);
    ConfigurationDocumentLocalFile.inProcessQueues.set(lockKey, nextInQueue);

    try {
      const waitRes = await this.waitForInProcessGate(prev, start, signal);
      if (!waitRes.ok) {
        return waitRes;
      }

      const parentDir = path.dirname(this._lockPath);
      const dirRes = ProtectedFileSystem.createDirectory(parentDir);
      if (!dirRes.ok) {
        return dirRes;
      }

      const lockHandleResult = await this.acquireCrossProcessLock(start, signal);
      if (!lockHandleResult.ok) {
        return lockHandleResult;
      }

      const lockInfo = lockHandleResult.value;
      try {
        return await action();
      } finally {
        this.releaseCrossProcessLock(lockInfo);
      }
    } finally {
      releaseQueue();
      if (ConfigurationDocumentLocalFile.inProcessQueues.get(lockKey) === nextInQueue) {
        ConfigurationDocumentLocalFile.inProcessQueues.delete(lockKey);
      }
    }
  }

  private async waitForInProcessGate(
    prev: Promise<unknown>,
    startMs: number,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    const remaining = Math.max(1, this._lockTimeoutMs - (Date.now() - startMs));
    let timeoutHandle: ReturnType<typeof setTimeout> | undefined;
    let onAbort: (() => void) | undefined;

    try {
      const raceResult = await Promise.race([
        prev.then(() => "acquired" as const, () => "acquired" as const),
        new Promise<"timeout">((resolve) => {
          timeoutHandle = setTimeout(() => resolve("timeout"), remaining);
        }),
        ...(signal
          ? [
              new Promise<"aborted">((_, reject) => {
                if (signal.aborted) {
                  reject(signal.reason ?? new DOMException("The operation was aborted.", "AbortError"));
                  return;
                }
                onAbort = () =>
                  reject(signal.reason ?? new DOMException("The operation was aborted.", "AbortError"));
                signal.addEventListener("abort", onAbort, { once: true });
              })
            ]
          : [])
      ]);

      if (raceResult === "timeout") {
        return fail(
          `configuration: could not take the lock on '${this._documentPath}' within ${(this._lockTimeoutMs / 1000).toFixed(3)}s`,
          "Unavailable"
        );
      }
      return ok(undefined);
    } finally {
      if (timeoutHandle !== undefined) {
        clearTimeout(timeoutHandle);
      }
      if (signal && onAbort) {
        signal.removeEventListener("abort", onAbort);
      }
    }
  }

  private async acquireCrossProcessLock(
    startMs: number,
    signal?: AbortSignal
  ): Promise<Result<{ readonly fd: number; readonly createdExclusively: boolean }>> {
    let backoffMs = 5;

    while (true) {
      signal?.throwIfAborted();

      // First try O_CREAT | O_EXCL ('wx+') so multiple Node processes contend even if .NET isn't holding FileShare.None.
      // If the file already exists (e.g. left on disk by .NET FileStream), check if a .NET process holds FileShare.None
      // or if a live Node process recorded its PID inside.
      try {
        const fd = fs.openSync(this._lockPath, "wx+", 0o600);
        try {
          fs.writeFileSync(fd, `node:${process.pid}:${Date.now()}`, "utf8");
        } catch {
          // ignore
        }
        return ok({ fd, createdExclusively: true });
      } catch (err: unknown) {
        const code = (err as { code?: string }).code;
        if (code === "EEXIST") {
          // File exists on disk. Could be held by .NET (FileShare.None -> openSync('r+') throws EBUSY)
          // or left behind by .NET (openSync('r+') succeeds and content is empty), or held by a Node process.
          try {
            const fd = fs.openSync(this._lockPath, "r+");
            let content = "";
            try {
              const buf = Buffer.alloc(128);
              const bytesRead = fs.readSync(fd, buf, 0, buf.length, 0);
              content = buf.subarray(0, bytesRead).toString("utf8").trim();
            } catch {
              // ignore
            }

            if (content.startsWith("node:")) {
              const parts = content.split(":");
              const holderPid = Number.parseInt(parts[1] ?? "", 10);
              fs.closeSync(fd);
              if (!Number.isNaN(holderPid) && this.isProcessAlive(holderPid)) {
                // Held by another live Node process
              } else {
                // Stale Node lock file; remove and retry
                try {
                  fs.unlinkSync(this._lockPath);
                } catch {
                  // ignore
                }
                continue;
              }
            } else {
              // Empty .lock file left by .NET (since we succeeded in opening it with 'r+', .NET does not hold FileShare.None on it).
              try {
                fs.ftruncateSync(fd, 0);
                fs.writeSync(fd, `node:${process.pid}:${Date.now()}`, 0, "utf8");
              } catch {
                // ignore
              }
              return ok({ fd, createdExclusively: false });
            }
          } catch (openErr: unknown) {
            const openCode = (openErr as { code?: string }).code;
            if (openCode === "ENOENT") {
              continue;
            }
            if (openCode !== "EBUSY" && openCode !== "EPERM" && openCode !== "EACCES") {
              const message = openErr instanceof Error ? openErr.message : String(openErr);
              return fail(`Lock acquisition failed unexpectedly: ${message}`, "Unexpected");
            }
            // Contended by .NET FileShare.None! Fall through to backoff.
          }
        } else if (code !== "EBUSY" && code !== "EPERM" && code !== "EACCES") {
          const message = err instanceof Error ? err.message : String(err);
          return fail(`Lock acquisition failed unexpectedly: ${message}`, "Forbidden");
        }
      }

      const elapsed = Date.now() - startMs;
      if (elapsed >= this._lockTimeoutMs) {
        return fail(
          `configuration: could not take the lock on '${this._documentPath}' within ${(elapsed / 1000).toFixed(3)}s`,
          "Unavailable"
        );
      }

      const remaining = this._lockTimeoutMs - elapsed;
      await this.delay(Math.min(backoffMs, remaining), signal);
      backoffMs = Math.min(backoffMs * 2, 50);
    }
  }

  private releaseCrossProcessLock(lockInfo: { readonly fd: number; readonly createdExclusively: boolean }): void {
    try {
      if (!lockInfo.createdExclusively) {
        try {
          fs.ftruncateSync(lockInfo.fd, 0);
        } catch {
          // ignore
        }
      }
      fs.closeSync(lockInfo.fd);
    } catch {
      // ignore
    }

    if (lockInfo.createdExclusively) {
      try {
        fs.unlinkSync(this._lockPath);
      } catch {
        // ignore
      }
    }
  }

  private isProcessAlive(pid: number): boolean {
    try {
      process.kill(pid, 0);
      return true;
    } catch (err: unknown) {
      const code = (err as { code?: string }).code;
      return code === "EPERM";
    }
  }

  private delay(ms: number, signal?: AbortSignal): Promise<void> {
    return new Promise((resolve, reject) => {
      if (signal?.aborted) {
        reject(signal.reason ?? new DOMException("The operation was aborted.", "AbortError"));
        return;
      }
      let onAbort: (() => void) | undefined;
      const timer = setTimeout(() => {
        if (signal && onAbort) {
          signal.removeEventListener("abort", onAbort);
        }
        resolve();
      }, ms);

      if (signal) {
        onAbort = () => {
          clearTimeout(timer);
          reject(signal.reason ?? new DOMException("The operation was aborted.", "AbortError"));
        };
        signal.addEventListener("abort", onAbort, { once: true });
      }
    });
  }
}
