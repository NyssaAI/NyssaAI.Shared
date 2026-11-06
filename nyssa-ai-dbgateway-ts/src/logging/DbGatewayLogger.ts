// Responsibility: Structured operational logging contract and Pino emission bridge for database boundary events.
// Public interface:
// +-- DbGatewayLogger
// |   +-- debug(operation, details)
// |   +-- error(operation, details, error)
// |   +-- info(operation, details)
// |   `-- warn(operation, details)
// `-- emitGatewayLog(customLogger, level, operation, details, error)

import { getLogger } from "@nyssa-ai/logging";

export interface DbGatewayLogger {
  info(operation: string, details?: Record<string, unknown>): void;
  warn(operation: string, details?: Record<string, unknown>): void;
  error(operation: string, details?: Record<string, unknown>, error?: unknown): void;
  debug(operation: string, details?: Record<string, unknown>): void;
}

const gatewayPinoLogger = getLogger("nyssa_ai_dbgateway");

/** Emit a database gateway diagnostic record to the process-wide Pino logger and optional custom logger. */
export function emitGatewayLog(
  customLogger: DbGatewayLogger | undefined,
  level: "debug" | "info" | "warn" | "error",
  operation: string,
  details?: Record<string, unknown>,
  error?: unknown
): void {
  try {
    if (customLogger) {
      if (level === "info") customLogger.info(operation, details);
      else if (level === "warn") customLogger.warn(operation, details);
      else if (level === "error") customLogger.error(operation, details, error);
      else customLogger.debug(operation, details);
    }

    const fields: Record<string, unknown> = { operation, ...(details ?? {}) };
    if (level === "info") {
      gatewayPinoLogger.info("Database operation {operation} ({outcome})", fields, operation);
    } else if (level === "warn") {
      gatewayPinoLogger.warn("Database operation {operation} degraded", fields, operation);
    } else if (level === "error") {
      gatewayPinoLogger.error("Database operation {operation} failed", error, fields, operation);
    } else {
      gatewayPinoLogger.debug("Database operation {operation} ({outcome})", fields, operation);
    }
  } catch {
    // TS-LOG: Sink failures never interrupt database operations
  }
}

