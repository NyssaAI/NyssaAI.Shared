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
});
