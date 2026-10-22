import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
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
});
