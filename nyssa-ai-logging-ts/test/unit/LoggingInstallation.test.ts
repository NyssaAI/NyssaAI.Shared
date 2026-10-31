import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import { createNyssaLogging } from "../../src/configuration/createNyssaLogging.js";
import { LoggingBuilder } from "../../src/configuration/LoggingBuilder.js";
import { TempLogWorkspace } from "./helpers/TempLogWorkspace.js";
import { StderrCapture } from "./helpers/StderrCapture.js";
import { StdoutCapture } from "./helpers/StdoutCapture.js";
import { FixedClock } from "./helpers/FixedClock.js";

describe("LoggingInstallation", () => {
  it("createNyssaLogging_withFile_configuresBothDestinations", () => {
    const workspace = new TempLogWorkspace();
    const stderr = new StderrCapture();
    try {
      const logging = createNyssaLogging(workspace.logPath, "Information");
      const logger = logging.createLogger("OrderService");

      logger.info("Order {OrderId} placed", { OrderId: 1042 });
      logging.dispose();

      expect(stderr.text).toContain("Order 1042 placed");
      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Order 1042 placed");
      expect(records[0]?.AdditionalFields["OrderId"]).toBe(1042);
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("withRotatingJsonLog_datedFile_prefixesCurrentUtcDate", () => {
    const workspace = new TempLogWorkspace();
    const clock = new FixedClock(new Date("2026-10-13T12:00:00.000Z"));
    try {
      const logging = new LoggingBuilder("Information", clock)
        .withRotatingJsonLog(path.join(workspace.directoryPath, "app.json"));
      const logger = logging.createLogger("PaymentService");

      logger.info("Payment received");
      logging.dispose();

      const expectedFileName = "2026.10.13-app.json";
      const expectedPath = path.join(workspace.directoryPath, expectedFileName);

      expect(fs.existsSync(expectedPath)).toBe(true);
      const records = workspace.readRecords(expectedPath);
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Payment received");
    } finally {
      workspace.dispose();
    }
  });

  it("withConsole_enabled_writesToStandardOutput", () => {
    const stdout = new StdoutCapture();
    try {
      const logging = new LoggingBuilder("Information").withConsole();
      const logger = logging.createLogger("CliCommand");

      logger.info("Task completed successfully");
      logging.dispose();

      expect(stdout.text).toContain("Task completed successfully");
    } finally {
      stdout.dispose();
    }
  });

  it("minimumLevel_filtering_dropsRecordsBelowThreshold", () => {
    const stderr = new StderrCapture();
    try {
      const logging = new LoggingBuilder("Warning").withStdErr();
      const logger = logging.createLogger("Worker");

      logger.debug("Debug event");
      logger.info("Info event");
      logger.warn("Warning event");
      logging.dispose();

      expect(stderr.text).not.toContain("Debug event");
      expect(stderr.text).not.toContain("Info event");
      expect(stderr.text).toContain("Warning event");
    } finally {
      stderr.dispose();
    }
  });
});
