// Responsibility: Provider for category-scoped standard output loggers.
// Public interface:
// `-- ConsoleOutputLoggerProvider
//     +-- createLogger(categoryName)
//     +-- dispose()
//     `-- new(minimumLevel)

import type { Logger } from "../Logger.js";
import type { LogLevel } from "../LogLevel.js";
import { ConsoleOutputLogger } from "./ConsoleOutputLogger.js";

export class ConsoleOutputLoggerProvider {
  private readonly minimumLevel: LogLevel;

  public constructor(minimumLevel?: LogLevel) {
    this.minimumLevel = minimumLevel ?? "Information";
  }

  public createLogger(categoryName: string): Logger {
    return new ConsoleOutputLogger(categoryName, this.minimumLevel);
  }

  public dispose(): void {
    // Stdout requires no resource teardown.
  }
}
