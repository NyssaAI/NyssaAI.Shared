// Responsibility: Category-scoped logger writing structured JSON log records to a LogFile.
// Public interface:
// `-- JsonFileLogger
//     +-- critical(message, error, fields, operation)
//     +-- debug(message, fields, operation)
//     +-- error(message, error, fields, operation)
//     +-- info(message, fields, operation)
//     +-- isEnabled(level)
//     +-- new(categoryName, logFile, clock, minimumLevel)
//     +-- trace(message, fields, operation)
//     `-- warn(message, fields, operation)

import { isLogLevelEnabled, type LogLevel } from "../LogLevel.js";
import type { Clock } from "./Clock.js";
import type { LogFile } from "./LogFile.js";
import type { LogRecord } from "./LogRecord.js";
import { StderrDiagnostics } from "./StderrDiagnostics.js";

export class JsonFileLogger {
  private readonly categoryName: string;
  private readonly logFile: LogFile;
  private readonly clock: Clock;
  private readonly minimumLevel: LogLevel;

  public constructor(
    categoryName: string,
    logFile: LogFile,
    clock: Clock,
    minimumLevel: LogLevel
  ) {
    this.categoryName = categoryName;
    this.logFile = logFile;
    this.clock = clock;
    this.minimumLevel = minimumLevel;
  }

  public isEnabled(level: LogLevel): boolean {
    return isLogLevelEnabled(level, this.minimumLevel);
  }

  public trace(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    this.write("Trace", message, fields, operation, null);
  }

  public debug(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    this.write("Debug", message, fields, operation, null);
  }

  public info(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    this.write("Information", message, fields, operation, null);
  }

  public warn(
    message: string,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    this.write("Warning", message, fields, operation, null);
  }

  public error(
    message: string,
    error?: unknown,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    this.write("Error", message, fields, operation, error);
  }

  public critical(
    message: string,
    error?: unknown,
    fields?: Record<string, unknown>,
    operation?: string
  ): void {
    this.write("Critical", message, fields, operation, error);
  }

  private write(
    level: LogLevel,
    message: string,
    fields: Record<string, unknown> | undefined,
    operation: string | undefined,
    error: unknown
  ): void {
    if (!this.isEnabled(level)) {
      return;
    }

    try {
      const formattedMessage = this.formatTemplate(message, fields);
      const exception = this.formatError(error);

      const record: LogRecord = {
        Timestamp: this.clock.now().toISOString(),
        Level: level,
        Message: formattedMessage,
        Module: this.categoryName,
        Operation: operation ?? "",
        Exception: exception,
        AdditionalFields: fields ?? {}
      };

      this.logFile.writeRecord(record);
    } catch (writeError) {
      const msg = writeError instanceof Error ? writeError.message : String(writeError);
      StderrDiagnostics.error(this.categoryName, `failed to log to file: ${msg}`);
    }
  }

  private formatTemplate(
    template: string,
    fields: Record<string, unknown> | undefined
  ): string {
    if (!fields || Object.keys(fields).length === 0) {
      return template;
    }
    return template.replace(/\{(\w+)\}/g, (match, key: string) => {
      if (key in fields && fields[key] !== undefined) {
        return String(fields[key]);
      }
      return match;
    });
  }

  private formatError(error: unknown): string | null {
    if (error === undefined || error === null) {
      return null;
    }
    if (error instanceof Error) {
      return error.stack ?? error.message;
    }
    return String(error);
  }
}
