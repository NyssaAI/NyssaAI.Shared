// Responsibility: In-process queue and cross-process file lock coordination compatible with .NET and Python .lock files.
// Public interface:
// `-- CrossProcessFileLock
//     +-- new(documentPath, lockPath, lockTimeoutMs)
//     `-- withLock(action, signal)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "./ProtectedFileSystem.js";

/**
 * CrossProcessFileLock coordinates in-process async queuing and OS-level cross-process
 * .lock files compatible with .NET FileShare.None and Python FileLock.
 */
export class CrossProcessFileLock {
  private static readonly inProcessQueues = new Map<string, Promise<unknown>>();

  private readonly _documentPath: string;
  private readonly _lockPath: string;
  private readonly _lockTimeoutMs: number;

  public constructor(documentPath: string, lockPath: string, lockTimeoutMs: number) {
    this._documentPath = documentPath;
    this._lockPath = lockPath;
    this._lockTimeoutMs = lockTimeoutMs;
  }

  public async withLock<T>(action: () => Promise<Result<T>>, signal?: AbortSignal): Promise<Result<T>> {
    signal?.throwIfAborted();
    const lockKey = process.platform === "win32" ? this._lockPath.toLowerCase() : this._lockPath;
    const start = Date.now();

    const prev = CrossProcessFileLock.inProcessQueues.get(lockKey) ?? Promise.resolve();
    let releaseQueue!: () => void;
    const currentGate = new Promise<void>((resolve) => {
      releaseQueue = resolve;
    });
    const nextInQueue = prev.then(
      () => currentGate,
      () => currentGate
    );
    CrossProcessFileLock.inProcessQueues.set(lockKey, nextInQueue);

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
      if (CrossProcessFileLock.inProcessQueues.get(lockKey) === nextInQueue) {
        CrossProcessFileLock.inProcessQueues.delete(lockKey);
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
        prev.then(
          () => "acquired" as const,
          () => "acquired" as const
        ),
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
                try {
                  fs.unlinkSync(this._lockPath);
                } catch {
                  // ignore
                }
                continue;
              }
            } else {
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
