import { describe, it, expect } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import { createNyssaLogging } from "../../src/configuration/createNyssaLogging.js";
import { LoggingBuilder } from "../../src/configuration/LoggingBuilder.js";
import type { Logger } from "../../src/Logger.js";
import { TempLogWorkspace } from "../unit/helpers/TempLogWorkspace.js";
import { StderrCapture } from "../unit/helpers/StderrCapture.js";
import { StdoutCapture } from "../unit/helpers/StdoutCapture.js";

// Domain fixture class matching C# OrderService
class OrderService {
  private readonly logger: Logger;

  public constructor(logger: Logger) {
    this.logger = logger;
  }

  public place(orderId: number): void {
    this.logger.info("Order {OrderId} placed", { OrderId: orderId });
  }
}

describe("LoggingTutorials", () => {
  it("write_logs_to_a_json_file", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logging = new LoggingBuilder("Information").addNyssaJsonFile(workspace.logPath);
      const orderService = new OrderService(logging.createLogger("OrderService"));

      orderService.place(1042);
      logging.dispose();

      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      const record = records[0]!;
      expect(record.Level).toBe("Information");
      expect(record.Message).toBe("Order 1042 placed");
      expect(record.Module).toBe("OrderService");
      expect(record.AdditionalFields["OrderId"]).toBe(1042);
    } finally {
      workspace.dispose();
    }
  });

  it("configure_logging_in_one_call", () => {
    const stderr = new StderrCapture();
    const workspace = new TempLogWorkspace();
    try {
      const logging = createNyssaLogging(workspace.logPath, "Information");
      const orderService = new OrderService(logging.createLogger("OrderService"));

      orderService.place(1042);
      logging.dispose();

      expect(stderr.text).toContain("Order 1042 placed");
      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Order 1042 placed");
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("log_to_standard_error_only", () => {
    const stderr = new StderrCapture();
    const workspace = new TempLogWorkspace();
    try {
      const logging = createNyssaLogging(undefined, "Information");
      const logger = logging.createLogger("LoggingTutorials");

      logger.info("Inventory checked for {Sku}", { Sku: "ABC-123" });
      logger.debug("This debug message must be filtered out");
      logging.dispose();

      expect(stderr.text).toContain("Inventory checked for ABC-123");
      expect(stderr.text).not.toContain("This debug message");
      expect(fs.existsSync(workspace.logPath)).toBe(false);
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("chain_destinations", () => {
    const stdout = new StdoutCapture();
    const stderr = new StderrCapture();
    const workspace = new TempLogWorkspace();
    try {
      const logging = createNyssaLogging()
        .withRotatingJsonLog(workspace.logPath)
        .withStdErr()
        .withConsole();

      const logger = logging.createLogger("LoggingTutorials");
      logger.info("Order {OrderId} placed", { OrderId: 1042 });
      logging.dispose();

      expect(stdout.text).toContain("Order 1042 placed");
      expect(stderr.text).toContain("Order 1042 placed");

      const dir = path.dirname(workspace.logPath);
      const baseName = path.basename(workspace.logPath);
      const files = fs.readdirSync(dir).filter(f => f.endsWith(`-${baseName}`));
      expect(files).toHaveLength(1);

      const records = workspace.readRecords(path.join(dir, files[0]!));
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Order 1042 placed");
    } finally {
      stdout.dispose();
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("log_structured_messages", () => {
    const workspace = new TempLogWorkspace();
    try {
      const logging = new LoggingBuilder("Information").addNyssaJsonFile(workspace.logPath);
      const logger = logging.createLogger("LoggingTutorials");

      logger.info(
        "Order {OrderId} accepted for {Amount}, shipping {Shipping} to {City}, {Region} {PostalCode}",
        {
          OrderId: 1042,
          Amount: 39.5,
          Shipping: "Express",
          City: "Seattle",
          Region: "WA",
          PostalCode: "98101"
        },
        "OrderAccepted"
      );
      logging.dispose();

      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      const record = records[0]!;

      expect(record.Operation).toBe("OrderAccepted");
      expect(record.AdditionalFields["OrderId"]).toBe(1042);
      expect(record.AdditionalFields["Amount"]).toBe(39.5);
      expect(record.AdditionalFields["Shipping"]).toBe("Express");
      expect(record.AdditionalFields["City"]).toBe("Seattle");
      expect(record.AdditionalFields["PostalCode"]).toBe("98101");
    } finally {
      workspace.dispose();
    }
  });

  it("choose_which_records_reach_the_file", () => {
    const stderr = new StderrCapture();
    const workspace = new TempLogWorkspace();
    try {
      const logging = new LoggingBuilder("Information")
        .withStdErr()
        .addNyssaJsonFile(workspace.logPath, "Warning");

      const logger = logging.createLogger("LoggingTutorials");
      logger.info("Inventory checked for {Sku}", { Sku: "ABC-123" });
      logger.warn("Inventory low for {Sku}", { Sku: "ABC-123" });
      logging.dispose();

      expect(stderr.text).toContain("Inventory checked for ABC-123");
      expect(stderr.text).toContain("Inventory low for ABC-123");

      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Inventory low for ABC-123");
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });

  it("keep_running_when_the_log_file_cannot_be_opened", () => {
    const stderr = new StderrCapture();
    const workspace = new TempLogWorkspace();
    try {
      const logging = new LoggingBuilder("Information")
        .addNyssaJsonFile("relative.log")
        .addNyssaJsonFile(workspace.logPath);

      const logger = logging.createLogger("LoggingTutorials");
      logger.warn("Still running");
      logging.dispose();

      expect(stderr.text).toContain("relative.log");
      expect(stderr.text).toContain("erro:");

      const records = workspace.readRecords();
      expect(records).toHaveLength(1);
      expect(records[0]?.Message).toBe("Still running");
    } finally {
      stderr.dispose();
      workspace.dispose();
    }
  });
});
