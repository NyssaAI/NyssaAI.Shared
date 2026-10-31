// Responsibility: Serialize a LogRecord to a single UTF-8 JSON line matching canonical schema.
// Public interface:
// `-- LogRecordSerializer
//     `-- serialize(record)

import type { LogRecord } from "./LogRecord.js";

function sanitizeFieldValue(value: unknown): unknown {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === "string" || typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : String(value);
  }
  if (typeof value === "bigint") {
    return value.toString();
  }
  if (value instanceof Date) {
    return value.toISOString();
  }
  if (value instanceof Error) {
    return value.stack ?? value.message;
  }
  if (typeof value === "object") {
    try {
      return JSON.parse(JSON.stringify(value));
    } catch {
      return String(value);
    }
  }
  return String(value);
}

export class LogRecordSerializer {
  public static serialize(record: LogRecord): string {
    const sanitizedFields: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(record.AdditionalFields)) {
      sanitizedFields[key] = sanitizeFieldValue(value);
    }

    const payload = {
      Timestamp: record.Timestamp,
      Level: record.Level,
      Message: record.Message,
      Module: record.Module,
      Operation: record.Operation,
      Exception: record.Exception,
      AdditionalFields: sanitizedFields
    };

    return JSON.stringify(payload);
  }
}
