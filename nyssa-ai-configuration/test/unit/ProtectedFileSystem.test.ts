import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { execFileSync } from "node:child_process";
import { ProtectedFileSystem } from "../../src/fileSecurity/ProtectedFileSystem.js";
import { DirectorySafety } from "../../src/fileSecurity/DirectorySafety.js";

describe("ProtectedFileSystem and DirectorySafety", () => {
  let tempDir: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-sec-test-"));
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("protectedFileSystem_newDirectoryOnPosix_sets0700Mode", () => {
    const targetDir = path.join(tempDir, "sub", "config");
    const createRes = ProtectedFileSystem.createDirectory(targetDir);

    expect(createRes.ok).toBe(true);
    expect(fs.existsSync(targetDir)).toBe(true);

    if (process.platform !== "win32") {
      const stat = fs.statSync(targetDir);
      const mode = stat.mode & 0o777;
      expect(mode).toBe(0o700);
    }
  });

  it("protectedFileSystem_newFileOnPosix_sets0600Mode", () => {
    const targetFile = path.join(tempDir, "secret.json");
    const createRes = ProtectedFileSystem.createFile(targetFile, "{\"test\":1}");

    expect(createRes.ok).toBe(true);
    expect(fs.existsSync(targetFile)).toBe(true);

    if (process.platform !== "win32") {
      const stat = fs.statSync(targetFile);
      const mode = stat.mode & 0o777;
      expect(mode).toBe(0o600);
    }
  });

  it("directorySafety_validDirectory_verifiesSuccessfully", () => {
    const targetDir = path.join(tempDir, "safe-dir");
    ProtectedFileSystem.createDirectory(targetDir);

    const verifyRes = DirectorySafety.verify(targetDir);
    expect(verifyRes.ok).toBe(true);
  });

  it("directorySafety_absentOrBlankDirectory_verifiesSuccessfully", () => {
    const absentDir = path.join(tempDir, "absent-dir");
    expect(DirectorySafety.verify(absentDir).ok).toBe(true);
    expect(DirectorySafety.verify("").ok).toBe(true);
    expect(DirectorySafety.verify("   ").ok).toBe(true);
  });

  it("directorySafety_worldWritableDirectory_failsWithRemediation", () => {
    const targetDir = path.join(tempDir, "unsafe-dir");
    ProtectedFileSystem.createDirectory(targetDir);

    if (process.platform === "win32") {
      execFileSync("icacls.exe", [targetDir, "/grant", "*S-1-1-0:(W)"], { stdio: "ignore" });
    } else {
      fs.chmodSync(targetDir, 0o777);
    }

    const verifyRes = DirectorySafety.verify(targetDir);
    expect(verifyRes.ok).toBe(false);
    if (!verifyRes.ok) {
      expect(verifyRes.category).toBe("Forbidden");
      expect(verifyRes.error).toContain("writable by");
      if (process.platform === "win32") {
        expect(verifyRes.error).toContain("icacls");
      } else {
        expect(verifyRes.error).toContain("chmod");
      }
    }
  });

  it("directorySafety_privateFileReadableByOthers_failsWithRemediation", () => {
    const filePath = path.join(tempDir, "config-manifest.json");
    ProtectedFileSystem.createFile(filePath, "{}");

    if (process.platform === "win32") {
      execFileSync("icacls.exe", [filePath, "/grant", "*S-1-1-0:(R)"], { stdio: "ignore" });
    } else {
      fs.chmodSync(filePath, 0o644);
    }

    const verifyRes = DirectorySafety.verifyPrivateFile(filePath);
    expect(verifyRes.ok).toBe(false);
    if (!verifyRes.ok) {
      expect(verifyRes.category).toBe("Forbidden");
      expect(verifyRes.error).toContain("is readable by");
      if (process.platform === "win32") {
        expect(verifyRes.error).toContain("icacls");
      } else {
        expect(verifyRes.error).toContain("chmod 600");
      }
    }
  });
});
