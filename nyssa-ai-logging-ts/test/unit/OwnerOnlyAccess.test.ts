import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { OwnerOnlyAccess } from "../../src/fileLogging/OwnerOnlyAccess.js";
import { TempLogWorkspace } from "./helpers/TempLogWorkspace.js";

describe("OwnerOnlyAccess", () => {
  it("createDirectory_newDirectory_createsDirectoryWithoutThrowing", () => {
    const workspace = new TempLogWorkspace();
    try {
      const subDir = path.join(workspace.directoryPath, "sub_logs");
      OwnerOnlyAccess.createDirectory(subDir);
      expect(fs.existsSync(subDir)).toBe(true);

      if (os.platform() !== "win32") {
        const stats = fs.statSync(subDir);
        expect(stats.mode & 0o777).toBe(0o700);
      }
    } finally {
      workspace.dispose();
    }
  });

  it("applyToFile_existingFile_setsPermissionsWithoutThrowing", () => {
    const workspace = new TempLogWorkspace();
    try {
      const testFile = path.join(workspace.directoryPath, "owner.log");
      fs.writeFileSync(testFile, "test data\n");
      OwnerOnlyAccess.applyToFile(testFile);

      if (os.platform() !== "win32") {
        const stats = fs.statSync(testFile);
        expect(stats.mode & 0o777).toBe(0o600);
      }
    } finally {
      workspace.dispose();
    }
  });
});
