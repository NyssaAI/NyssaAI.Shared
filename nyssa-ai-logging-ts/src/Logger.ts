// Responsibility: Structured operational logging contract for action-taking classes.
// Public interface:
// `-- Logger
//     +-- critical(message, error, fields, operation)
//     +-- debug(message, fields, operation)
//     +-- error(message, error, fields, operation)
//     +-- info(message, fields, operation)
//     +-- isEnabled(level)
//     +-- trace(message, fields, operation)
//     `-- warn(message, fields, operation)

import type { LogLevel } from "./LogLevel.js";

export interface Logger {
  trace(message: string, fields?: Record<string, unknown>, operation?: string): void;
  debug(message: string, fields?: Record<string, unknown>, operation?: string): void;
  info(message: string, fields?: Record<string, unknown>, operation?: string): void;
  warn(message: string, fields?: Record<string, unknown>, operation?: string): void;
  error(message: string, error?: unknown, fields?: Record<string, unknown>, operation?: string): void;
  critical(message: string, error?: unknown, fields?: Record<string, unknown>, operation?: string): void;
  isEnabled(level: LogLevel): boolean;
}
