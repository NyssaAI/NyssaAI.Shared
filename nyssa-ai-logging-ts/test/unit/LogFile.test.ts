import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import { LogFile } from "../../src/fileLogging/LogFile.js";
import { TempLogWorkspace } from "./helpers/TempLogWorkspace.js";
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

describe("LogFile", () => {
  it("initialize_validPath_createsDirectoryAndOpensFile", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logFile = new LogFile(workspace.logPath);
      const error = logFile.initialize();
      expect(error).toBeNull();
      expect(fs.existsSync(workspace.logPath)).toBe(true);
      logFile.dispose();
    } finally {
      workspace.dispose();
    }
  });

  it("initialize_invalidPath_returnsErrorMessageWithoutThrowing", () => {
    const logFile = new LogFile("relative.log");
    const error = logFile.initialize();
    expect(error).toContain("Invalid log path");
    logFile.dispose();
  });

  it("writeRecord_singleRecord_appendsRecordToDiskImmediately", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logFile = new LogFile(workspace.logPath);
      expect(logFile.initialize()).toBeNull();

      logFile.writeRecord(createRecord("First record"));
      logFile.dispose();

      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("First record");
    } finally {
      workspace.dispose();
    }
  });

  it("initialize_withSecondWriterOnSamePath_bothSucceed", () => {
    const workspace = new TempLogWorkspace();
    const first = new LogFile(workspace.logPath);
    const second = new LogFile(workspace.logPath);
    try {
      expect(first.initialize()).toBeNull();
      expect(second.initialize()).toBeNull();

      first.writeRecord(createRecord("from the first writer"));
      second.writeRecord(createRecord("from the second writer"));
      first.dispose();
      second.dispose();

      const content = fs.readFileSync(workspace.logPath, "utf8");
      expect(content).toContain("from the first writer");
      expect(content).toContain("from the second writer");
    } finally {
      first.dispose();
      second.dispose();
      workspace.dispose();
    }
  });

  it("initialize_withExpiredBackup_deletesItAndKeepsFreshOne", () => {
    const workspace = new TempLogWorkspace();
    const expired = `${workspace.logPath}.2`;
    const fresh = `${workspace.logPath}.1`;
    try {
      fs.writeFileSync(expired, "old", "utf8");
      const oldTime = new Date(Date.now() - (LogFile.RETENTION_DAYS + 1) * 24 * 60 * 60 * 1000);
      fs.utimesSync(expired, oldTime, oldTime);
      fs.writeFileSync(fresh, "recent", "utf8");

      const logFile = new LogFile(workspace.logPath);
      try {
        expect(logFile.initialize()).toBeNull();
        expect(fs.existsSync(expired)).toBe(false);
        expect(fs.existsSync(fresh)).toBe(true);
      } finally {
        logFile.dispose();
      }
    } finally {
      workspace.dispose();
    }
  });

  it("writeRecord_withStructuredFieldsAndException_writesAllFields", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logFile = new LogFile(workspace.logPath);
      expect(logFile.initialize()).toBeNull();

      logFile.writeRecord({
        Timestamp: new Date().toISOString(),
        Level: "Error",
        Message: "Operation failed",
        Module: "TestModule",
        Operation: "TestOp",
        Exception: "Error: Test error",
        AdditionalFields: { userId: 123, action: "login" }
      });
      logFile.dispose();

      const content = fs.readFileSync(workspace.logPath, "utf8");
      expect(content).toContain("Operation failed");
      expect(content).toContain("Error: Test error");
      expect(content).toContain("userId");
      expect(content).toContain("login");
    } finally {
      workspace.dispose();
    }
  });

  it("writeRecord_beforeInitialize_doesNothing", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logFile = new LogFile(workspace.logPath);
      try {
        logFile.writeRecord(createRecord("before initialize"));
        expect(fs.existsSync(workspace.logPath)).toBe(false);
      } finally {
        logFile.dispose();
      }
    } finally {
      workspace.dispose();
    }
  });

  it("writeRecord_afterDispose_isIgnoredWithoutThrowing", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logFile = new LogFile(workspace.logPath);
      expect(logFile.initialize()).toBeNull();
      logFile.writeRecord(createRecord("before disposal"));
      logFile.dispose();

      logFile.writeRecord(createRecord("after disposal"));

      const content = fs.readFileSync(workspace.logPath, "utf8");
      expect(content).toContain("before disposal");
      expect(content).not.toContain("after disposal");
    } finally {
      workspace.dispose();
    }
  });

  it("dispose_calledTwice_releasesTheFileAndKeepsWhatWasWritten", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logFile = new LogFile(workspace.logPath);
      expect(logFile.initialize()).toBeNull();
      logFile.writeRecord(createRecord("written before dispose"));

      logFile.dispose();
      logFile.dispose();

      const content = fs.readFileSync(workspace.logPath, "utf8");
      expect(content).toContain("written before dispose");
      fs.unlinkSync(workspace.logPath);
      expect(fs.existsSync(workspace.logPath)).toBe(false);
    } finally {
      workspace.dispose();
    }
  });
});
