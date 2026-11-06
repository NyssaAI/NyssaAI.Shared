// Responsibility: Convert Pino JSON log lines into canonical Nyssa LogRecord instances with template formatting.
// Public interface:
// `-- extractNyssaRecord(pinoPayload, fallbackTimestamp)

import { fromPinoLevel } from "../LogLevel.js";
import type { LogRecord } from "./LogRecord.js";

const PINO_RESERVED_KEYS = new Set([
  "level",
  "time",
  "pid",
  "hostname",
  "msg",
  "category",
  "nyssaOperation",
  "nyssaException",
  "nyssaTimestamp"
]);

function formatTemplate(
  template: string,
  fields: Readonly<Record<string, unknown>>
): string {
  if (!template || Object.keys(fields).length === 0) {
    return template;
  }
  return template.replace(/\{(\w+)\}/g, (match, key: string) => {
    if (key in fields && fields[key] !== undefined && fields[key] !== null) {
      return String(fields[key]);
    }
    return match;
  });
}

/**
 * Convert a parsed Pino JSON payload into a canonical Nyssa `LogRecord`.
 */
export function extractNyssaRecord(
  pinoPayload: Readonly<Record<string, unknown>>,
  fallbackTimestamp: string
): LogRecord {
  const rawLevel = pinoPayload["level"];
  const level =
    typeof rawLevel === "number" || typeof rawLevel === "string"
      ? fromPinoLevel(rawLevel)
      : "Information";

  const timestamp =
    typeof pinoPayload["nyssaTimestamp"] === "string"
      ? pinoPayload["nyssaTimestamp"]
      : typeof pinoPayload["time"] === "number"
        ? new Date(pinoPayload["time"]).toISOString()
        : fallbackTimestamp;

  const category =
    typeof pinoPayload["category"] === "string" ? pinoPayload["category"] : "root";

  const operation =
    typeof pinoPayload["nyssaOperation"] === "string"
      ? pinoPayload["nyssaOperation"]
      : "";

  const exception =
    typeof pinoPayload["nyssaException"] === "string"
      ? pinoPayload["nyssaException"]
      : null;

  const additionalFields: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(pinoPayload)) {
    if (!PINO_RESERVED_KEYS.has(key) && value !== undefined) {
      additionalFields[key] = value;
    }
  }

  const rawMsg = typeof pinoPayload["msg"] === "string" ? pinoPayload["msg"] : "";
  const formattedMessage = formatTemplate(rawMsg, additionalFields);

  return {
    Timestamp: timestamp,
    Level: level,
    Message: formattedMessage,
    Module: category,
    Operation: operation,
    Exception: exception,
    AdditionalFields: additionalFields
  };
}
