// Responsibility: Cross-process file locking and atomic modification of one namespace JSON document.
// Public interface:
// +-- ConfigurationDocumentLocalFile
// |   +-- modify(modifier, signal?)
// |   +-- read(signal?)
// |   `-- remove(key, signal?)

import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";

/**
 * ConfigurationDocumentLocalFile coordinates cross-process locking and atomic document mutations.
 * Supports .NET .lock files left on disk using handle-based locking and retry backoff.
 */
export class ConfigurationDocumentLocalFile {
  private readonly _documentPath: string;
  private readonly _lockPath: string;
  private readonly _lockTimeoutMs: number;

  public constructor(documentPath: string, lockTimeoutMs: number = 10000) {
    this._documentPath = path.resolve(documentPath);
    this._lockPath = `${this._documentPath}.lock`;
    this._lockTimeoutMs = lockTimeoutMs;
  }

  /**
   * Reads all configuration values in the document under the cross-process lock.
   */
  public async read(signal?: AbortSignal): Promise<Result<Record<string, ConfigurationValue>>> {
    return this.withLock(async () => {
      if (!fs.existsSync(this._documentPath)) {
        return ok({});
      }

      try {
        const raw = fs.readFileSync(this._documentPath, "utf8");
        if (!raw || raw.trim().length === 0) {
          return ok({});
        }
        const parsed = JSON.parse(raw) as Record<string, ConfigurationValue>;
        return ok(parsed);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        return fail(`Failed to read configuration document '${this._documentPath}': ${message}`);
      }
    }, signal);
  }

  /**
   * Modifies entries in the configuration document atomically under the cross-process lock.
   */
  public async modify(
    modifier: (current: Record<string, ConfigurationValue>) => Result<Record<string, ConfigurationValue>>,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    return this.withLock(async () => {
      let current: Record<string, ConfigurationValue> = {};
      if (fs.existsSync(this._documentPath)) {
        try {
          const raw = fs.readFileSync(this._documentPath, "utf8");
          if (raw && raw.trim().length > 0) {
            current = JSON.parse(raw);
          }
        } catch (err) {
          const message = err instanceof Error ? err.message : String(err);
          return fail(`Failed to read existing configuration for modification: ${message}`);
        }
      }

      const modResult = modifier({ ...current });
      if (!modResult.ok) {
        return fail(modResult.error);
      }

      const updated = modResult.value;
      const json = JSON.stringify(updated, null, 2);

      // Unique sibling temp file to prevent write collisions
      const tempPath = `${this._documentPath}.${crypto.randomUUID()}.tmp`;
      try {
        ProtectedFileSystem.createFile(tempPath, json);
        fs.renameSync(tempPath, this._documentPath);
        return ok(undefined);
      } catch (err) {
        try {
          if (fs.existsSync(tempPath)) fs.unlinkSync(tempPath);
        } catch {
          // ignore
        }
        const message = err instanceof Error ? err.message : String(err);
        return fail(`Failed to write updated configuration: ${message}`);
      }
    }, signal);
  }

  /**
   * Acquires the cross-process lock and executes the action.
   */
  private async withLock<T>(action: () => Promise<Result<T>>, signal?: AbortSignal): Promise<Result<T>> {
    const parentDir = path.dirname(this._lockPath);
    ProtectedFileSystem.createDirectory(parentDir);

    const lockHandleResult = await this.acquireLock(signal);
    if (!lockHandleResult.ok) {
      return fail(lockHandleResult.error);
    }

    const fd = lockHandleResult.value;
    try {
      return await action();
    } finally {
      try {
        fs.closeSync(fd);
      } catch {
        // ignore close errors
      }
    }
  }

  /**
   * Tries to acquire handle on the lock file with backoff.
   */
  private async acquireLock(signal?: AbortSignal): Promise<Result<number>> {
    const start = Date.now();
    let backoffMs = 5;

    while (Date.now() - start < this._lockTimeoutMs) {
      if (signal?.aborted) {
        return fail(`Lock acquisition aborted: ${signal.reason}`);
      }

      try {
        // Open with read-write flags. On Windows, if open with exclusive sharing, subsequent opens fail with EBUSY/EPERM.
        // We open with 'r+' if exists or 'w+' to open/create.
        const fd = fs.openSync(this._lockPath, "w+");
        return ok(fd);
      } catch (err: unknown) {
        const code = (err as { code?: string }).code;
        if (code === "EBUSY" || code === "EPERM" || code === "EACCES") {
          // Contended by another process
          await this.delay(backoffMs);
          backoffMs = Math.min(backoffMs * 1.5, 200);
          continue;
        }
        const message = err instanceof Error ? err.message : String(err);
        return fail(`Lock acquisition failed unexpectedly: ${message}`);
      }
    }

    return fail(`Could not acquire lock on '${this._lockPath}' within ${this._lockTimeoutMs}ms.`);
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
