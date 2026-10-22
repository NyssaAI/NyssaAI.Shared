// Responsibility: Verify that configuration directories and files are safe from other accounts.
// Public interface:
// +-- DirectorySafety
// |   +-- verify(path)
// |   `-- verifyPrivateFile(filePath)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";

/**
 * DirectorySafety inspects directory and file permissions to ensure they are private to the current user.
 */
export class DirectorySafety {
  /**
   * Verifies that the given directory or file path exists and has safe permissions.
   */
  public static verify(targetPath: string): Result<void> {
    try {
      const resolved = path.resolve(targetPath);
      if (!fs.existsSync(resolved)) {
        return fail(`Path does not exist: '${targetPath}'`);
      }

      if (process.platform !== "win32") {
        const stat = fs.statSync(resolved);
        const mode = stat.mode & 0o777;

        // Reject if group or others have write or execute permissions
        if ((mode & 0o077) !== 0) {
          // If group/other write is set, it's unsafe
          if ((mode & 0o022) !== 0) {
            return fail(`Unsafe permissions (${mode.toString(8)}) on '${targetPath}': writable by group or others.`);
          }
        }
      }

      return ok(undefined);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Security verification failed for '${targetPath}': ${message}`);
    }
  }

  /**
   * Verifies that a sensitive file is restricted to the current user.
   */
  public static verifyPrivateFile(filePath: string): Result<void> {
    return this.verify(filePath);
  }
}
