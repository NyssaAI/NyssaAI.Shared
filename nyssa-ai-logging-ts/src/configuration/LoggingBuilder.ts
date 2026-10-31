// Responsibility: Fluent builder for configuring logging destinations and creating loggers.
// Public interface:
// `-- LoggingBuilder
//     +-- addNyssaJsonFile(filePath, minimumLevel)
//     +-- addProvider(provider)
//     +-- createLogger(categoryName)
//     +-- dispose()
//     +-- new(minimumLevel, clock)
//     +-- setMinimumLevel(level)
//     +-- withConsole(minimumLevel)
//     +-- withRotatingJsonLog(filePath, minimumLevel)
//     `-- withStdErr(minimumLevel)

import * as path from "node:path";
import type { Logger } from "../Logger.js";
import type { LoggerProvider } from "../LoggerProvider.js";
import type { LogLevel } from "../LogLevel.js";
import type { Clock } from "../fileLogging/Clock.js";
import { SystemClock } from "../fileLogging/SystemClock.js";
import { JsonFileLoggerProvider } from "../fileLogging/JsonFileLoggerProvider.js";
import { StderrOutputLoggerProvider } from "../consoleLogging/StderrOutputLoggerProvider.js";
import { ConsoleOutputLoggerProvider } from "../consoleLogging/ConsoleOutputLoggerProvider.js";
import { CompositeLogger } from "./CompositeLogger.js";

function formatUtcDate(date: Date): string {
  const y = date.getUTCFullYear();
  const m = String(date.getUTCMonth() + 1).padStart(2, "0");
  const d = String(date.getUTCDate()).padStart(2, "0");
  return `${y}.${m}.${d}`;
}

export class LoggingBuilder {
  private minimumLevel: LogLevel;
  private readonly clock: Clock;
  private readonly providers: LoggerProvider[] = [];
  private hasStderr = false;
  private hasConsole = false;
  private disposed = false;

  public constructor(minimumLevel?: LogLevel, clock?: Clock) {
    this.minimumLevel = minimumLevel ?? "Information";
    this.clock = clock ?? new SystemClock();
  }

  public setMinimumLevel(level: LogLevel): this {
    this.minimumLevel = level;
    return this;
  }

  public withStdErr(minimumLevel?: LogLevel): this {
    if (!this.hasStderr) {
      this.hasStderr = true;
      this.providers.push(new StderrOutputLoggerProvider(minimumLevel ?? this.minimumLevel));
    }
    return this;
  }

  public withConsole(minimumLevel?: LogLevel): this {
    if (!this.hasConsole) {
      this.hasConsole = true;
      this.providers.push(new ConsoleOutputLoggerProvider(minimumLevel ?? this.minimumLevel));
    }
    return this;
  }

  public withRotatingJsonLog(filePath: string, minimumLevel?: LogLevel): this {
    const directory = path.dirname(filePath);
    const effectiveDir = !directory || directory === "." ? process.cwd() : directory;
    const date = formatUtcDate(this.clock.now());
    const destination = path.join(effectiveDir, `${date}-${path.basename(filePath)}`);
    return this.addFileDestination(destination, minimumLevel);
  }

  public addNyssaJsonFile(filePath: string, minimumLevel?: LogLevel): this {
    return this.addFileDestination(filePath, minimumLevel);
  }

  public addProvider(provider: LoggerProvider): this {
    this.providers.push(provider);
    return this;
  }

  public createLogger(categoryName: string): Logger {
    const loggers = this.providers.map(p => p.createLogger(categoryName));
    return new CompositeLogger(loggers);
  }

  public dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    for (const provider of this.providers) {
      provider.dispose();
    }
  }

  private addFileDestination(destination: string, minimumLevel?: LogLevel): this {
    const provider = JsonFileLoggerProvider.tryCreate(
      destination,
      this.clock,
      minimumLevel ?? this.minimumLevel
    );
    if (provider !== null) {
      this.providers.push(provider);
    }
    return this;
  }
}
