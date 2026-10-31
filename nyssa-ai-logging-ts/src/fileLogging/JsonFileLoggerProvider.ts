// Responsibility: Factory and lifecycle manager for rotating JSON file logger instances.
// Public interface:
// `-- JsonFileLoggerProvider
//     +-- createLogger(categoryName)
//     +-- dispose()
//     +-- new(logFile, clock, minimumLevel)
//     `-- tryCreate(filePath, clock, minimumLevel)

import type { Logger } from "../Logger.js";
import type { LogLevel } from "../LogLevel.js";
import type { Clock } from "./Clock.js";
import { SystemClock } from "./SystemClock.js";
import { JsonFileLogger } from "./JsonFileLogger.js";
import { LogFile } from "./LogFile.js";
import { StderrDiagnostics } from "./StderrDiagnostics.js";

export class JsonFileLoggerProvider {
  private readonly logFile: LogFile;
  private readonly clock: Clock;
  private readonly minimumLevel: LogLevel;
  private disposed = false;

  public constructor(
    logFile: LogFile,
    clock: Clock,
    minimumLevel: LogLevel
  ) {
    this.logFile = logFile;
    this.clock = clock;
    this.minimumLevel = minimumLevel;
  }

  public static tryCreate(
    filePath: string,
    clock?: Clock,
    minimumLevel?: LogLevel
  ): JsonFileLoggerProvider | null {
    if (!filePath || filePath.trim().length === 0) {
      return null;
    }

    const effectiveClock = clock ?? new SystemClock();
    const logFile = new LogFile(filePath, undefined, effectiveClock);
    const error = logFile.initialize();
    if (error !== null) {
      logFile.dispose();
      StderrDiagnostics.error(filePath, error);
      return null;
    }

    return new JsonFileLoggerProvider(
      logFile,
      effectiveClock,
      minimumLevel ?? "Trace"
    );
  }

  public createLogger(categoryName: string): Logger {
    return new JsonFileLogger(
      categoryName,
      this.logFile,
      this.clock,
      this.minimumLevel
    );
  }

  public dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    this.logFile.dispose();
  }
}
