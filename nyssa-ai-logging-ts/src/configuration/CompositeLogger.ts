// Responsibility: Dispatch structured log messages to multiple underlying logger instances.
// Public interface:
// `-- CompositeLogger
//     +-- critical(message, error, fields, operation)
//     +-- debug(message, fields, operation)
//     +-- error(message, error, fields, operation)
//     +-- info(message, fields, operation)
//     +-- isEnabled(level)
//     +-- new(loggers)
//     +-- trace(message, fields, operation)
//     `-- warn(message, fields, operation)

import type { Logger } from "../Logger.js";
import type { LogLevel } from "../LogLevel.js";

export class CompositeLogger {
  private readonly loggers: readonly Logger[];

  public constructor(loggers: readonly Logger[]) {
    this.loggers = loggers;
  }

  public isEnabled(level: LogLevel): boolean {
    return this.loggers.some(logger => logger.isEnabled(level));
  }

  public trace(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    for (const logger of this.loggers) {
      logger.trace(message, fields, operation);
    }
  }

  public debug(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    for (const logger of this.loggers) {
      logger.debug(message, fields, operation);
    }
  }

  public info(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    for (const logger of this.loggers) {
      logger.info(message, fields, operation);
    }
  }

  public warn(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    for (const logger of this.loggers) {
      logger.warn(message, fields, operation);
    }
  }

  public error(
    message: string,
    error?: unknown,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    for (const logger of this.loggers) {
      logger.error(message, error, fields, operation);
    }
  }

  public critical(
    message: string,
    error?: unknown,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    for (const logger of this.loggers) {
      logger.critical(message, error, fields, operation);
    }
  }
}
