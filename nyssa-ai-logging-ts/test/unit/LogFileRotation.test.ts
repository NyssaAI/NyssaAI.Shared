import { describe, it, expect, vi } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import { LogFile } from "../../src/fileLogging/LogFile.js";
import { TempLogWorkspace } from "./helpers/TempLogWorkspace.js";
import { FixedClock } from "./helpers/FixedClock.js";
import { StderrCapture } from "./helpers/StderrCapture.js";
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

function countRotationFailures(stderrLines: string[]): number {
  return stderrLines
    .join("")
    .split("\n")
    .filter(line => line.includes("failed to rotate log file")).length;
}

function countReopenFailures(stderrLines: string[]): number {
  return stderrLines
    .join("")
    .split("\n")
    .filter(line => line.includes("failed to reopen log file")).length;
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
  }, 15_000);

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
  }, 15_000);

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
  }, 15_000);

  it.each([
    [64, 1],
    [1024 * 1024, 0]
  ])(
    "writeRecord_rotatesOnlyOnceFilePassesTrigger (%i bytes -> %i backups)",
    (triggerBytes, expectedBackups) => {
      const workspace = new TempLogWorkspace();
      try {
        const log = new LogFile(workspace.logPath, triggerBytes);
        expect(log.initialize()).toBeNull();

        log.writeRecord(createRecord("first"));
        log.writeRecord(createRecord("second"));
        log.dispose();

        expect(countBackups(workspace.logPath)).toBe(expectedBackups);
      } finally {
        workspace.dispose();
      }
    }
  );

  it("writeRecord_whenRotationFails_reportsOnceThenBacksOffByOneTrigger", () => {
    const trigger = 1024;
    const workspace = new TempLogWorkspace();
    const stderrLines: string[] = [];
    const stderrSpy = vi
      .spyOn(process.stderr, "write")
      .mockImplementation((chunk: string | Uint8Array): boolean => {
        stderrLines.push(String(chunk));
        return true;
      });
    try {
      fs.mkdirSync(`${workspace.logPath}.1`, { recursive: true });
      const log = new LogFile(workspace.logPath, trigger);
      expect(log.initialize()).toBeNull();
      const big = "x".repeat(trigger + 100);

      log.writeRecord(createRecord(big)); // over the trigger, no attempt yet
      log.writeRecord(createRecord("second")); // attempt 1 fails; back off one trigger
      log.writeRecord(createRecord("third")); // inside the back-off window
      log.writeRecord(createRecord("fourth")); // still inside
      expect(countRotationFailures(stderrLines)).toBe(1);

      log.writeRecord(createRecord(big)); // pushes past the retry threshold
      log.writeRecord(createRecord("sixth")); // attempt 2
      log.dispose();
      expect(countRotationFailures(stderrLines)).toBe(2);

      const content = fs.readFileSync(workspace.logPath, "utf8");
      for (const message of ["second", "third", "fourth", "sixth"]) {
        expect(content).toContain(message);
      }
    } finally {
      stderrSpy.mockRestore();
      workspace.dispose();
    }
  });

  it("writeRecord_whenRotationAndReopenFail_resumesAfterAccessIsRestored", () => {
    const clock = new FixedClock(new Date("2026-10-13T12:00:00.000Z"));
    const workspace = new TempLogWorkspace();
    const stderr = new StderrCapture();
    try {
      const log = new LogFile(workspace.logPath, 64, clock);
      expect(log.initialize()).toBeNull();
      log.writeRecord(createRecord("before outage"));

      const displacedPath = `${workspace.logPath}.displaced`;
      fs.renameSync(workspace.logPath, displacedPath);
      fs.mkdirSync(workspace.logPath, { recursive: true });
      log.writeRecord(createRecord("during outage"));
      expect(countRotationFailures([stderr.text])).toBe(1);
      expect(countReopenFailures([stderr.text])).toBe(1);

      for (let i = 0; i < 10; i++) {
        log.writeRecord(createRecord("still unavailable"));
      }
      expect(countReopenFailures([stderr.text])).toBe(1);

      clock.advance(60_000);
      log.writeRecord(createRecord("retry while unavailable"));
      expect(countReopenFailures([stderr.text])).toBe(2);

      fs.rmdirSync(workspace.logPath);
      fs.renameSync(displacedPath, workspace.logPath);
      clock.advance(60_000);
      log.writeRecord(createRecord("after recovery"));
      log.dispose();

      const content = fs.readFileSync(workspace.logPath, "utf8");
      expect(content).toContain("before outage");
      expect(content).toContain("after recovery");
      expect(content).not.toContain("during outage");
      expect(countReopenFailures([stderr.text])).toBe(2);
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("writeRecord_whenDisposedAfterRotationAndReopenFail_doesNotReopen", () => {
    const clock = new FixedClock(new Date("2026-10-13T12:00:00.000Z"));
    const workspace = new TempLogWorkspace();
    const stderr = new StderrCapture();
    try {
      const log = new LogFile(workspace.logPath, 64, clock);
      expect(log.initialize()).toBeNull();
      log.writeRecord(createRecord("before outage"));

      fs.renameSync(workspace.logPath, `${workspace.logPath}.displaced`);
      fs.mkdirSync(workspace.logPath, { recursive: true });
      log.writeRecord(createRecord("during outage"));
      expect(countReopenFailures([stderr.text])).toBe(1);
      log.dispose();

      fs.rmdirSync(workspace.logPath);
      clock.advance(60_000);
      log.writeRecord(createRecord("after disposal"));

      expect(fs.existsSync(workspace.logPath)).toBe(false);
      expect(countReopenFailures([stderr.text])).toBe(1);
    } finally {
      stderr.dispose();
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
