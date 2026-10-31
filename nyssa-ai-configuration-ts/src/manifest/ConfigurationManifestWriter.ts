// Responsibility: Write and provision application sections in the configuration manifest atomically under lock.
// Public interface:
// `-- ConfigurationManifestWriter
//     +-- DEFAULT_LOCK_TIMEOUT_MS
//     `-- setConfigurationDirectory(manifestPath, applicationName, configurationDirectory, lockTimeoutMs)

import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import { ConfigurationManifestReader } from "./ConfigurationManifestReader.js";
import type { ConfigurationManifest } from "./ConfigurationManifest.js";
import type { ApplicationManifestEntry } from "./ApplicationManifestEntry.js";

/**
 * ConfigurationManifestWriter updates configuration manifest sections atomically under a lock file.
 */
export class ConfigurationManifestWriter {
  public static readonly DEFAULT_LOCK_TIMEOUT_MS = 10000;

  /**
   * Sets or creates the configuration directory for an application in the manifest.
   */
  public static setConfigurationDirectory(
    manifestPath: string,
    applicationName: string,
    configurationDirectory: string,
    lockTimeoutMs?: number
  ): Result<string> {
    const resolvedPath = path.resolve(manifestPath);
    const parentDir = path.dirname(resolvedPath);
    const timeoutMs = lockTimeoutMs ?? this.DEFAULT_LOCK_TIMEOUT_MS;

    const dirRes = ProtectedFileSystem.createDirectory(parentDir);
    if (!dirRes.ok) {
      return dirRes;
    }

    const safeRes = DirectorySafety.verify(parentDir);
    if (!safeRes.ok) {
      return safeRes;
    }

    const lockRes = this.acquireLockSync(resolvedPath, timeoutMs);
    if (!lockRes.ok) {
      return lockRes;
    }

    const lockInfo = lockRes.value;
    try {
      let manifest: ConfigurationManifest = {
        version: ConfigurationManifestReader.SUPPORTED_VERSION,
        applications: {}
      };

      if (fs.existsSync(resolvedPath)) {
        const readRes = ConfigurationManifestReader.readManifest(resolvedPath);
        if (!readRes.ok) {
          return readRes;
        }
        manifest = readRes.value;
      }

      const apps: Record<string, ApplicationManifestEntry> = {
        ...(manifest.applications ?? {})
      };

      const existingSection = apps[applicationName] ?? {};
      const updatedSection: ApplicationManifestEntry = {
        ...existingSection,
        configurationDirectory,
        ...(lockTimeoutMs !== undefined
          ? { lockTimeoutMs: Math.round(lockTimeoutMs) }
          : existingSection.lockTimeoutMs !== undefined
            ? { lockTimeoutMs: existingSection.lockTimeoutMs }
            : existingSection.lockTimeout !== undefined
              ? { lockTimeout: existingSection.lockTimeout }
              : { lockTimeoutMs: this.DEFAULT_LOCK_TIMEOUT_MS })
      };

      apps[applicationName] = updatedSection;

      const updatedManifest: ConfigurationManifest = {
        version: manifest.version || ConfigurationManifestReader.SUPPORTED_VERSION,
        applications: apps
      };

      const tempFile = `${resolvedPath}.tmp.${crypto.randomUUID().replace(/-/g, "")}`;
      try {
        const json = JSON.stringify(updatedManifest, null, 2);
        const writeRes = ProtectedFileSystem.createFile(tempFile, json);
        if (!writeRes.ok) {
          return writeRes;
        }
        fs.renameSync(tempFile, resolvedPath);
        return ok(configurationDirectory);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        return fail(`manifest: writing '${manifestPath}' failed: ${message}`, "Forbidden");
      } finally {
        try {
          if (fs.existsSync(tempFile)) {
            fs.unlinkSync(tempFile);
          }
        } catch {
          // ignore cleanup failure
        }
      }
    } finally {
      this.releaseLockSync(resolvedPath, lockInfo);
    }
  }

  private static acquireLockSync(
    manifestPath: string,
    timeoutMs: number
  ): Result<{ readonly fd: number; readonly createdExclusively: boolean }> {
    const lockPath = `${manifestPath}.lock`;
    const start = Date.now();
    let backoffMs = 5;

    while (true) {
      try {
        const fd = fs.openSync(lockPath, "wx+", 0o600);
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
            const fd = fs.openSync(lockPath, "r+");
            let content = "";
            try {
              const buf = Buffer.alloc(128);
              const bytesRead = fs.readSync(fd, buf, 0, buf.length, 0);
              content = buf.subarray(0, bytesRead).toString("utf8").trim();
            } catch {
              // ignore
            }

            if (content.startsWith("node:")) {
              const holderPid = Number.parseInt(content.split(":")[1] ?? "", 10);
              fs.closeSync(fd);
              if (!Number.isNaN(holderPid) && this.isProcessAlive(holderPid)) {
                // held by live process
              } else {
                try {
                  fs.unlinkSync(lockPath);
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
              return fail(`manifest: writing '${manifestPath}' failed: ${message}`, "Forbidden");
            }
          }
        } else if (code !== "EBUSY" && code !== "EPERM" && code !== "EACCES") {
          const message = err instanceof Error ? err.message : String(err);
          return fail(`manifest: writing '${manifestPath}' failed: ${message}`, "Forbidden");
        }
      }

      const elapsed = Date.now() - start;
      if (elapsed >= timeoutMs) {
        return fail(
          `manifest: could not take the lock on '${manifestPath}' within ${(elapsed / 1000).toFixed(3)}s`,
          "Unavailable"
        );
      }

      const sleepMs = Math.min(backoffMs, timeoutMs - elapsed);
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, sleepMs);
      backoffMs = Math.min(backoffMs * 2, 50);
    }
  }

  private static releaseLockSync(
    manifestPath: string,
    lockInfo: { readonly fd: number; readonly createdExclusively: boolean }
  ): void {
    const lockPath = `${manifestPath}.lock`;
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
        fs.unlinkSync(lockPath);
      } catch {
        // ignore
      }
    }
  }

  private static isProcessAlive(pid: number): boolean {
    try {
      process.kill(pid, 0);
      return true;
    } catch (err: unknown) {
      const code = (err as { code?: string }).code;
      return code === "EPERM";
    }
  }
}
