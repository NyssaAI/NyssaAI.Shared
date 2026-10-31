// Responsibility: Enforce owner-only access controls on library-created log directories and files.
// Public interface:
// `-- OwnerOnlyAccess
//     +-- OWNER_READ_WRITE
//     +-- OWNER_READ_WRITE_EXECUTE
//     +-- applyToFile(filePath)
//     `-- createDirectory(directoryPath)

import * as fs from "node:fs";
import * as os from "node:os";
import { execFileSync } from "node:child_process";
import { StderrDiagnostics } from "./StderrDiagnostics.js";

export class OwnerOnlyAccess {
  public static readonly OWNER_READ_WRITE = 0o600;
  public static readonly OWNER_READ_WRITE_EXECUTE = 0o700;

  public static createDirectory(directoryPath: string): void {
    if (fs.existsSync(directoryPath)) {
      return;
    }

    try {
      if (os.platform() === "win32") {
        fs.mkdirSync(directoryPath, { recursive: true });
        this.applyWindowsAcl(directoryPath, true);
        return;
      }

      fs.mkdirSync(directoryPath, { recursive: true, mode: this.OWNER_READ_WRITE_EXECUTE });
      this.restrictUnixPath(directoryPath, this.OWNER_READ_WRITE_EXECUTE);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      StderrDiagnostics.warning(directoryPath, `failed to restrict directory permissions: ${message}`);
    }
  }

  public static applyToFile(filePath: string): void {
    try {
      if (os.platform() === "win32") {
        this.applyWindowsAcl(filePath, false);
        return;
      }

      this.restrictUnixPath(filePath, this.OWNER_READ_WRITE);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      StderrDiagnostics.warning(filePath, `failed to restrict file permissions: ${message}`);
    }
  }

  private static restrictUnixPath(targetPath: string, mode: number): void {
    try {
      fs.chmodSync(targetPath, mode);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      StderrDiagnostics.warning(targetPath, `failed to restrict permissions: ${message}`);
    }
  }

  private static applyWindowsAcl(targetPath: string, isDirectory: boolean): void {
    try {
      const username = process.env["USERNAME"] || process.env["USER"];
      if (!username) {
        return;
      }
      const flag = isDirectory ? "(OI)(CI)F" : "F";
      execFileSync(
        "icacls.exe",
        [
          targetPath,
          "/inheritance:r",
          "/grant:r",
          `${username}:${flag}`,
          "/grant:r",
          `SYSTEM:${flag}`,
          "/grant:r",
          `Administrators:${flag}`
        ],
        { stdio: "ignore" }
      );
    } catch {
      // Best-effort on Windows; do not fail host process if icacls is unavailable.
    }
  }
}
