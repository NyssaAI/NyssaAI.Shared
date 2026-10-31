// Responsibility: Provider for category-scoped standard error loggers.
// Public interface:
// `-- StderrOutputLoggerProvider
//     +-- createLogger(categoryName)
//     +-- dispose()
//     `-- new(minimumLevel)

import type { Logger } from "../Logger.js";
import type { LogLevel } from "../LogLevel.js";
import { StderrOutputLogger } from "./StderrOutputLogger.js";

export class StderrOutputLoggerProvider {
  private readonly minimumLevel: LogLevel;

  public constructor(minimumLevel?: LogLevel) {
    this.minimumLevel = minimumLevel ?? "Information";
  }

  public createLogger(categoryName: string): Logger {
    return new StderrOutputLogger(categoryName, this.minimumLevel);
  }

  public dispose(): void {
    // Stderr requires no resource teardown.
  }
}
