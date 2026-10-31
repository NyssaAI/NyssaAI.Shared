import { describe, it, expect } from "vitest";
import { LogRecordSerializer } from "../../src/fileLogging/LogRecordSerializer.js";
import type { LogRecord } from "../../src/fileLogging/LogRecord.js";

describe("LogRecordSerializer", () => {
  it("serialize_validRecord_emitsExpectedJsonStructure", () => {
    const record: LogRecord = {
      Timestamp: "2026-10-13T12:00:00.000Z",
      Level: "Information",
      Message: "Order 1042 placed",
      Module: "OrderService",
      Operation: "OrderPlaced",
      Exception: null,
      AdditionalFields: {
        OrderId: 1042,
        Customer: "Alice",
        Active: true
      }
    };

    const jsonString = LogRecordSerializer.serialize(record);
    const parsed = JSON.parse(jsonString) as Record<string, unknown>;

    expect(parsed["Timestamp"]).toBe("2026-10-13T12:00:00.000Z");
    expect(parsed["Level"]).toBe("Information");
    expect(parsed["Message"]).toBe("Order 1042 placed");
    expect(parsed["Module"]).toBe("OrderService");
    expect(parsed["Operation"]).toBe("OrderPlaced");
    expect(parsed["Exception"]).toBeNull();

    const fields = parsed["AdditionalFields"] as Record<string, unknown>;
    expect(fields["OrderId"]).toBe(1042);
    expect(fields["Customer"]).toBe("Alice");
    expect(fields["Active"]).toBe(true);
  });

  it("serialize_variousFieldTypes_preservesTypesSafely", () => {
    const error = new Error("database unavailable");
    const date = new Date("2026-10-13T12:00:00.000Z");

    const record: LogRecord = {
      Timestamp: date.toISOString(),
      Level: "Error",
      Message: "Failed to connect",
      Module: "DatabaseClient",
      Operation: "Connect",
      Exception: error.stack ?? error.message,
      AdditionalFields: {
        timeoutMs: 5000,
        retries: 3n,
        attemptDate: date,
        innerError: error,
        metadata: { host: "localhost", port: 5432 }
      }
    };

    const jsonString = LogRecordSerializer.serialize(record);
    const parsed = JSON.parse(jsonString) as Record<string, unknown>;
    const fields = parsed["AdditionalFields"] as Record<string, unknown>;

    expect(fields["timeoutMs"]).toBe(5000);
    expect(fields["retries"]).toBe("3");
    expect(fields["attemptDate"]).toBe(date.toISOString());
    expect(typeof fields["innerError"]).toBe("string");
    expect((fields["metadata"] as Record<string, unknown>)["host"]).toBe("localhost");
  });
});
