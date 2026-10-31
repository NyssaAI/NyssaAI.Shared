// Responsibility: Output formatted operational log lines to standard output with sink isolation.
// Public interface:
// `-- ConsoleOutputLogger
//     +-- critical(message, error, fields, operation)
//     +-- debug(message, fields, operation)
//     +-- error(message, error, fields, operation)
//     +-- info(message, fields, operation)
//     +-- isEnabled(level)
//     +-- new(categoryName, minimumLevel)
//     +-- trace(message, fields, operation)
//     `-- warn(message, fields, operation)

import * as os from "node:os";
import { isLogLevelEnabled, type LogLevel } from "../LogLevel.js";

const LEVEL_TAGS: Readonly<Record<LogLevel, string>> = {
  Trace: "trce",
  Debug: "dbug",
  Information: "info",
  Warning: "warn",
  Error: "fail",
  Critical: "crit",
  None: "none"
};

export class ConsoleOutputLogger {
  private readonly categoryName: string;
  private readonly minimumLevel: LogLevel;

  public constructor(categoryName: string, minimumLevel: LogLevel) {
    this.categoryName = categoryName;
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
    _operation: string | undefined,
    error: unknown
  ): void {
    if (!this.isEnabled(level)) {
      return;
    }

    try {
      const tag = LEVEL_TAGS[level] ?? "info";
      const formatted = this.formatTemplate(message, fields);
      let output = `${tag}: ${this.categoryName}${os.EOL}      ${formatted}${os.EOL}`;

      if (error !== undefined && error !== null) {
        const errorText = error instanceof Error ? error.stack ?? error.message : String(error);
        output += `      ${errorText}${os.EOL}`;
      }

      process.stdout.write(output);
    } catch {
      // TS-LOG: Sink failures are contained and do not fail the business operation.
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
}
