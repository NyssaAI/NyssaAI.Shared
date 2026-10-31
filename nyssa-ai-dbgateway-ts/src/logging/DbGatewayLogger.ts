// Responsibility: Structured operational logging contract for database boundary events.
// Public interface:
// `-- DbGatewayLogger
//     +-- debug(operation, details)
//     +-- error(operation, details, error)
//     +-- info(operation, details)
//     `-- warn(operation, details)

export interface DbGatewayLogger {
  info(operation: string, details?: Record<string, unknown>): void;
  warn(operation: string, details?: Record<string, unknown>): void;
  error(operation: string, details?: Record<string, unknown>, error?: unknown): void;
  debug(operation: string, details?: Record<string, unknown>): void;
}
