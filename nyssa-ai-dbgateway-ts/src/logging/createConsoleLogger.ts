// Responsibility: Create a safe structured console logger with sink failure containment.
// Public interface:
// `-- createConsoleLogger(minLevel)

import type { DbGatewayLogger } from "./DbGatewayLogger.js";

const LEVEL_SEVERITY: Record<string, number> = {
  debug: 0,
  info: 1,
  warn: 2,
  error: 3
};

export function createConsoleLogger(
  minLevel: "debug" | "info" | "warn" | "error" = "info"
): DbGatewayLogger {
  const minSeverity = LEVEL_SEVERITY[minLevel] ?? 1;

  const emit = (
    level: "debug" | "info" | "warn" | "error",
    operation: string,
    details?: Record<string, unknown>,
    error?: unknown
  ): void => {
    if ((LEVEL_SEVERITY[level] ?? 1) < minSeverity) {
      return;
    }
    try {
      const payload: Record<string, unknown> = {
        timestamp: new Date().toISOString(),
        severity: level,
        operation,
        ...details
      };
      if (error) {
        payload["error"] = error instanceof Error ? error.message : String(error);
      }
      const line = JSON.stringify(payload);
      if (level === "error") {
        console.error(line);
      } else if (level === "warn") {
        console.warn(line);
      } else {
        console.log(line);
      }
    } catch {
      // TS-LOG: Sink failures are contained and do not fail the business operation.
    }
  };

  return {
    info: (op, details) => emit("info", op, details),
    warn: (op, details) => emit("warn", op, details),
    error: (op, details, err) => emit("error", op, details, err),
    debug: (op, details) => emit("debug", op, details)
  };
}
