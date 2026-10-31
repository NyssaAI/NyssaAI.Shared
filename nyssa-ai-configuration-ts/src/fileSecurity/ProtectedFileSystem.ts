// Responsibility: Create directories and files protected to the current user account across POSIX and Windows.
// Public interface:
// `-- ProtectedFileSystem
//     +-- createDirectory(directoryPath)
//     `-- createFile(filePath, content)

import * as fs from "node:fs";
import * as path from "node:path";
import { execFileSync } from "node:child_process";
import { ok, fail, type Result } from "../results/Result.js";

/**
 * ProtectedFileSystem creates directories with 0700 and files with 0600 on POSIX,
 * and on Windows strips inherited parent DACLs to restrict control to the current user/SYSTEM.
 */
export class ProtectedFileSystem {
  private static readonly DIRECTORY_MODE_POSIX = 0o700;
  private static readonly FILE_MODE_POSIX = 0o600;

  /**
   * Creates a directory readable and writable only by the current account.
   */
  public static createDirectory(directoryPath: string): Result<string> {
    if (!directoryPath || directoryPath.trim().length === 0) {
      return fail("Directory path cannot be blank.", "Validation");
    }

    try {
      const resolved = path.resolve(directoryPath);
      if (!fs.existsSync(resolved)) {
        fs.mkdirSync(resolved, { recursive: true, mode: this.DIRECTORY_MODE_POSIX });

        if (process.platform === "win32") {
          this.protectWindowsPath(resolved, true);
        } else {
          try {
            fs.chmodSync(resolved, this.DIRECTORY_MODE_POSIX);
          } catch {
            // best-effort chmod on non-Windows
          }
        }
      }

      return ok(resolved);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to create protected directory '${directoryPath}': ${message}`, "Forbidden");
    }
  }

  /**
   * Creates a file readable and writable only by the current account.
   */
  public static createFile(filePath: string, content: string): Result<string> {
    if (!filePath || filePath.trim().length === 0) {
      return fail("File path cannot be blank.", "Validation");
    }

    try {
      const resolved = path.resolve(filePath);
      const parentDir = path.dirname(resolved);
      const dirResult = this.createDirectory(parentDir);
      if (!dirResult.ok) {
        return dirResult;
      }

      fs.writeFileSync(resolved, content, {
        encoding: "utf8",
        mode: this.FILE_MODE_POSIX,
        flag: "w"
      });

      if (process.platform === "win32") {
        this.protectWindowsPath(resolved, false);
      } else {
        try {
          fs.chmodSync(resolved, this.FILE_MODE_POSIX);
        } catch {
          // ignore
        }
      }

      return ok(resolved);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to write '${filePath}': ${message}`, "Forbidden");
    }
  }

  private static protectWindowsPath(targetPath: string, isDirectory: boolean): void {
    try {
      const username = process.env["USERNAME"] || process.env["USER"];
      if (!username) return;

      const grantFlag = isDirectory ? "(OI)(CI)F" : "F";
      execFileSync(
        "icacls.exe",
        [
          targetPath,
          "/inheritance:r",
          "/grant:r",
          `${username}:${grantFlag}`,
          "/grant:r",
          `SYSTEM:${grantFlag}`,
          "/grant:r",
          `Administrators:${grantFlag}`
        ],
        { stdio: "ignore" }
      );
    } catch {
      // Best-effort ACL stripping on Windows if icacls fails or in non-admin context
    }
  }
}
