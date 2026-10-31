import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import { LogFile } from "../../src/fileLogging/LogFile.js";
import { TempLogWorkspace } from "./helpers/TempLogWorkspace.js";
import { FixedClock } from "./helpers/FixedClock.js";
import type { LogRecord } from "../../src/fileLogging/LogRecord.js";

function createRecord(message: string): LogRecord {
  return {
    Timestamp: new Date().toISOString(),
    Level: "Information",
    Message: message,
    Module: "TestModule",
    Operation: "TestOp",
    Exception: null,
    AdditionalFields: {}
  };
}

function countBackups(basePath: string): number {
  const dir = path.dirname(basePath);
  const baseName = path.basename(basePath);
  let count = 0;
  for (let index = 1; index <= LogFile.BACKUP_RETENTION + 5; index++) {
    if (fs.existsSync(path.join(dir, `${baseName}.${index}`))) {
      count++;
    }
  }
  return count;
}

describe("LogFileRotation", () => {
  it("writeRecord_whenTheFilePassesTheTrigger_archivesItAndStartsANewOne", () => {
    const workspace = new TempLogWorkspace();
    try {
      const log = new LogFile(workspace.logPath, 64);
      expect(log.initialize()).toBeNull();

      log.writeRecord(createRecord("first record"));
      log.writeRecord(createRecord("second record"));
      log.dispose();

      const archivedPath = `${workspace.logPath}.1`;
      expect(fs.existsSync(archivedPath)).toBe(true);

      const archivedContent = fs.readFileSync(archivedPath, "utf8");
      const activeContent = fs.readFileSync(workspace.logPath, "utf8");

      expect(archivedContent).toContain("first record");
      expect(activeContent).toContain("second record");
      expect(activeContent).not.toContain("first record");
    } finally {
      workspace.dispose();
    }
  });

  it("writeRecord_whenRotatingRepeatedly_shiftsBackupsOldestLast", () => {
    const workspace = new TempLogWorkspace();
    try {
      const log = new LogFile(workspace.logPath, 64);
      expect(log.initialize()).toBeNull();

      log.writeRecord(createRecord("oldest"));
      log.writeRecord(createRecord("middle"));
      log.writeRecord(createRecord("newest"));
      log.dispose();

      const backup2 = fs.readFileSync(`${workspace.logPath}.2`, "utf8");
      const backup1 = fs.readFileSync(`${workspace.logPath}.1`, "utf8");
      const active = fs.readFileSync(workspace.logPath, "utf8");

      expect(backup2).toContain("oldest");
      expect(backup1).toContain("middle");
      expect(active).toContain("newest");
    } finally {
      workspace.dispose();
    }
  });

  it("writeRecord_accumulatesBackupsUpToRetention", () => {
    const workspace = new TempLogWorkspace();
    try {
      const log = new LogFile(workspace.logPath, 64);
      expect(log.initialize()).toBeNull();

      for (let i = 0; i < LogFile.BACKUP_RETENTION + 3; i++) {
        log.writeRecord(createRecord(`entry ${i}`));
      }
      log.dispose();

      expect(countBackups(workspace.logPath)).toBe(LogFile.BACKUP_RETENTION);
      expect(fs.existsSync(`${workspace.logPath}.${LogFile.BACKUP_RETENTION + 1}`)).toBe(false);
    } finally {
      workspace.dispose();
    }
  });

  it("deleteExpiredBackups_prunesBackupsPastRetentionWindow", () => {
    const workspace = new TempLogWorkspace();
    const clock = new FixedClock(new Date("2026-10-13T12:00:00.000Z"));
    try {
      const log = new LogFile(workspace.logPath, 64, clock);
      expect(log.initialize()).toBeNull();

      log.writeRecord(createRecord("old backup candidate"));
      log.writeRecord(createRecord("active entry"));
      log.dispose();

      const backup1 = `${workspace.logPath}.1`;
      expect(fs.existsSync(backup1)).toBe(true);

      // Advance clock by 31 days (retention is 30 days)
      clock.advanceDays(31);

      // Set file modified time to 31 days ago
      const oldTime = new Date("2026-09-10T12:00:00.000Z");
      fs.utimesSync(backup1, oldTime, oldTime);

      // Re-initialize logfile with clock 31 days ahead
      const nextLog = new LogFile(workspace.logPath, 64, clock);
      expect(nextLog.initialize()).toBeNull();
      nextLog.dispose();

      // Expired backup should now be deleted
      expect(fs.existsSync(backup1)).toBe(false);
    } finally {
      workspace.dispose();
    }
  });
});
