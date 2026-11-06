import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import { configureNyssaLogging } from "../../src/configuration/configureNyssaLogging.js";
import { createNyssaLogging } from "../../src/configuration/createNyssaLogging.js";
import { LoggingBuilder } from "../../src/configuration/LoggingBuilder.js";
import { getLogger } from "../../src/getLogger.js";
import { startSpan } from "../../src/telemetry/startSpan.js";
import { TempLogWorkspace } from "./helpers/TempLogWorkspace.js";
import { StderrCapture } from "./helpers/StderrCapture.js";
import { StdoutCapture } from "./helpers/StdoutCapture.js";
import { FixedClock } from "./helpers/FixedClock.js";

describe("LoggingInstallation", () => {
  it("configureNyssaLogging_default_createsDatedLogFileInLogsDirAndEmitsFromGetLogger", () => {
    const workspace = new TempLogWorkspace();
    const stderr = new StderrCapture();
    const clock = new FixedClock(new Date("2026-11-05T12:00:00.000Z"));
    try {
      const logDir = path.join(workspace.directoryPath, "logs");
      const logging = configureNyssaLogging({
        appName: "billing-worker",
        logDir,
        minimumLevel: "Information",
        clock
      });

      const logger = getLogger("billing.orders");
      logger.info("Order {OrderId} placed", { OrderId: 1042 });
      logging.dispose();

      expect(stderr.text).toContain("Order 1042 placed");
      const expectedPath = path.join(logDir, "2026.11.05-billing-worker-log.json");
      expect(fs.existsSync(expectedPath)).toBe(true);
      const records = workspace.readRecords(expectedPath);
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Order 1042 placed");
      expect(records[0]?.Module).toBe("billing.orders");
      expect(records[0]?.AdditionalFields["OrderId"]).toBe(1042);
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("configureNyssaLogging_packageLevels_filtersHierarchicallyByPrefix", () => {
    const workspace = new TempLogWorkspace();
    const stderr = new StderrCapture();
    try {
      const logging = configureNyssaLogging({
        filePath: workspace.logPath,
        minimumLevel: "Information",
        packageLevels: {
          "nyssa_ai_dbgateway": "Debug",
          "noisy.lib": "Error"
        }
      });

      getLogger("app.service").debug("app debug dropped");
      getLogger("nyssa_ai_dbgateway.sqlite").debug("sql query executed", { rows: 3 });
      getLogger("noisy.lib.http").warn("noisy warning dropped");
      getLogger("noisy.lib.http").error("noisy error kept");
      logging.dispose();

      const records = workspace.readRecords();
      expect(records).toHaveLength(2);
      expect(records[0]?.Message).toBe("sql query executed");
      expect(records[1]?.Message).toBe("noisy error kept");
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("startSpan_correlatesTraceAndSpanIdsAcrossAsyncLogs", async () => {
    const workspace = new TempLogWorkspace();
    const stderr = new StderrCapture();
    try {
      const logging = configureNyssaLogging({
        filePath: workspace.logPath,
        minimumLevel: "Information",
        enableOtel: true
      });
      const logger = getLogger("billing.checkout");

      await startSpan("checkout-order", { OrderId: 1042 }, "OrderCheckout", async outerSpan => {
        logger.info("Validating cart");
        await startSpan("charge-card", { Gateway: "Stripe" }, "CardCharge", async () => {
          logger.info("Card authorized");
        });
        expect(outerSpan.traceId).toHaveLength(32);
      });

      logging.dispose();

      const records = workspace.readRecords();
      expect(records).toHaveLength(2);
      const first = records[0]!;
      const second = records[1]!;

      expect(first.Operation).toBe("OrderCheckout");
      expect(first.AdditionalFields["OrderId"]).toBe(1042);
      expect(typeof first.AdditionalFields["TraceId"]).toBe("string");
      expect(typeof first.AdditionalFields["SpanId"]).toBe("string");

      expect(second.Operation).toBe("CardCharge");
      expect(second.AdditionalFields["Gateway"]).toBe("Stripe");
      expect(second.AdditionalFields["TraceId"]).toBe(first.AdditionalFields["TraceId"]);
      expect(second.AdditionalFields["ParentSpanId"]).toBe(first.AdditionalFields["SpanId"]);
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

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

