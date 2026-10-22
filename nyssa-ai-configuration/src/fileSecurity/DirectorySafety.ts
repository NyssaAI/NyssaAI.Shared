// Responsibility: Verify that configuration directories and files are safe from other accounts.
// Public interface:
// `-- DirectorySafety
//     +-- verify(targetPath)
//     +-- verifyFile(filePath)
//     `-- verifyPrivateFile(filePath)

import * as fs from "node:fs";
import * as path from "node:path";
import { execFileSync } from "node:child_process";
import { ok, fail, type Result } from "../results/Result.js";

/**
 * DirectorySafety inspects directory and file permissions to ensure they are private to the current user.
 */
export class DirectorySafety {
  private static readonly MULTI_USER_IDENTITIES = [
    "Everyone",
    "Authenticated Users",
    "NT AUTHORITY\\Authenticated Users",
    "BUILTIN\\Users",
    "Users",
    "BUILTIN\\Guests",
    "Guests",
    "*S-1-1-0",
    "S-1-1-0",
    "*S-1-5-11",
    "S-1-5-11",
    "*S-1-5-32-545",
    "S-1-5-32-545",
    "*S-1-5-32-546",
    "S-1-5-32-546"
  ];

  /**
   * Verifies that a directory is safe to read configuration from or write it to.
   * Non-existent or blank paths succeed because resolution creates them securely.
   */
  public static verify(targetPath: string): Result<void> {
    if (!targetPath || targetPath.trim().length === 0) {
      return ok(undefined);
    }

    const resolved = path.resolve(targetPath);
    if (!fs.existsSync(resolved)) {
      return ok(undefined);
    }

    return this.verifyWritable(resolved, targetPath);
  }

  /**
   * Verifies that a file is safe from modification by other accounts on the machine.
   */
  public static verifyFile(filePath: string): Result<void> {
    if (!filePath || filePath.trim().length === 0) {
      return ok(undefined);
    }

    const resolved = path.resolve(filePath);
    if (!fs.existsSync(resolved)) {
      return ok(undefined);
    }

    return this.verifyWritable(resolved, filePath);
  }

  /**
   * Verifies that a sensitive file is neither writable nor readable by other accounts.
   */
  public static verifyPrivateFile(filePath: string): Result<void> {
    const writable = this.verifyFile(filePath);
    if (!writable.ok) {
      return writable;
    }

    if (!filePath || filePath.trim().length === 0) {
      return ok(undefined);
    }

    const resolved = path.resolve(filePath);
    if (!fs.existsSync(resolved)) {
      return ok(undefined);
    }

    try {
      if (process.platform === "win32") {
        return this.verifyReadableWindows(resolved, filePath);
      }
      return this.verifyReadableUnix(resolved, filePath);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(
        `configuration: could not read permissions on '${filePath}': ${message}`,
        "Forbidden"
      );
    }
  }

  private static verifyWritable(resolvedPath: string, displayPath: string): Result<void> {
    try {
      if (process.platform === "win32") {
        return this.verifyWritableWindows(resolvedPath, displayPath);
      }
      return this.verifyWritableUnix(resolvedPath, displayPath);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(
        `configuration: could not read permissions on '${displayPath}': ${message}`,
        "Forbidden"
      );
    }
  }

  private static verifyWritableUnix(resolvedPath: string, displayPath: string): Result<void> {
    const stat = fs.statSync(resolvedPath);
    const mode = stat.mode & 0o777;
    const fixCmd = stat.isDirectory() ? `chmod 700 '${displayPath}'` : `chmod 600 '${displayPath}'`;

    if ((mode & 0o002) !== 0) {
      return fail(
        `configuration: '${displayPath}' is writable by other (o+w) — fix with: ${fixCmd}`,
        "Forbidden"
      );
    }

    if ((mode & 0o020) !== 0) {
      return fail(
        `configuration: '${displayPath}' is writable by group (g+w) — fix with: ${fixCmd}`,
        "Forbidden"
      );
    }

    return ok(undefined);
  }

  private static verifyReadableUnix(resolvedPath: string, displayPath: string): Result<void> {
    const stat = fs.statSync(resolvedPath);
    const mode = stat.mode & 0o777;
    const fix = `chmod 600 '${displayPath}'`;

    if ((mode & 0o005) !== 0) {
      return fail(
        `manifest: '${displayPath}' is readable by other (o+r): every account on this machine; it carries vault secrets, so startup is refused until it is private — fix with: ${fix}`,
        "Forbidden"
      );
    }

    if ((mode & 0o050) !== 0) {
      return fail(
        `manifest: '${displayPath}' is readable by group (g+r): every member of the file's group; it carries vault secrets, so startup is refused until it is private — fix with: ${fix}`,
        "Forbidden"
      );
    }

    return ok(undefined);
  }

  private static verifyWritableWindows(resolvedPath: string, displayPath: string): Result<void> {
    const aclLines = this.readWindowsIcacls(resolvedPath);
    for (const entry of aclLines) {
      if (entry.isDeny) {
        continue;
      }
      if (!this.isMultiUserIdentity(entry.identity)) {
        continue;
      }
      if (this.grantsWindowsWrite(entry.rights)) {
        return fail(
          `configuration: '${displayPath}' is writable by ${entry.identity} — fix with: icacls "${displayPath}" /inheritance:r /grant:r "%USERNAME%:F"`,
          "Forbidden"
        );
      }
    }
    return ok(undefined);
  }

  private static verifyReadableWindows(resolvedPath: string, displayPath: string): Result<void> {
    const aclLines = this.readWindowsIcacls(resolvedPath);
    for (const entry of aclLines) {
      if (entry.isDeny) {
        continue;
      }
      if (!this.isMultiUserIdentity(entry.identity)) {
        continue;
      }
      if (this.grantsWindowsRead(entry.rights)) {
        return fail(
          `manifest: '${displayPath}' is readable by ${entry.identity} (every account in that group); it carries vault secrets, so startup is refused until it is private — fix with: icacls "${displayPath}" /inheritance:r /grant:r "%USERNAME%:F"`,
          "Forbidden"
        );
      }
    }
    return ok(undefined);
  }

  private static readWindowsIcacls(resolvedPath: string): Array<{
    readonly identity: string;
    readonly isDeny: boolean;
    readonly rights: ReadonlyArray<string>;
  }> {
    const raw = execFileSync("icacls.exe", [resolvedPath], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"]
    });

    const lines = raw.split(/\r?\n/);
    const entries: Array<{
      readonly identity: string;
      readonly isDeny: boolean;
      readonly rights: ReadonlyArray<string>;
    }> = [];

    for (let i = 0; i < lines.length; i++) {
      let line = lines[i] ?? "";
      if (i === 0 && line.startsWith(resolvedPath)) {
        line = line.slice(resolvedPath.length);
      }
      const trimmed = line.trim();
      if (!trimmed || trimmed.startsWith("Successfully processed")) {
        continue;
      }

      const colonIdx = trimmed.indexOf(":(");
      if (colonIdx <= 0) {
        continue;
      }

      const identity = trimmed.slice(0, colonIdx).trim();
      const flagsPart = trimmed.slice(colonIdx + 1);
      const tokens: string[] = [];
      const tokenRegex = /\(([^)]+)\)/g;
      let match: RegExpExecArray | null;
      while ((match = tokenRegex.exec(flagsPart)) !== null) {
        if (match[1]) {
          tokens.push(match[1]);
        }
      }

      const isDeny = tokens.some((t) => t.toUpperCase() === "DENY" || t.toUpperCase() === "N");
      const lastToken = tokens[tokens.length - 1] ?? "";
      const rights = lastToken
        .split(",")
        .map((r) => r.trim().toUpperCase())
        .filter((r) => r.length > 0);

      entries.push({ identity, isDeny, rights });
    }

    return entries;
  }

  private static isMultiUserIdentity(identity: string): boolean {
    const normalized = identity.trim().toLowerCase();
    return this.MULTI_USER_IDENTITIES.some((candidate) => candidate.toLowerCase() === normalized);
  }

  private static grantsWindowsWrite(rights: ReadonlyArray<string>): boolean {
    const writeTokens = new Set(["F", "M", "W", "WD", "AD", "WDAC", "WO", "GW", "GA"]);
    return rights.some((r) => writeTokens.has(r));
  }

  private static grantsWindowsRead(rights: ReadonlyArray<string>): boolean {
    const readTokens = new Set(["F", "M", "RX", "R", "RD", "GR", "GA"]);
    return rights.some((r) => readTokens.has(r));
  }
}
