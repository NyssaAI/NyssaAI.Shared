// Responsibility: Fluent builder configuring Pino destinations, per-package log levels, and OpenTelemetry bridging.
// Public interface:
// `-- LoggingBuilder
//     +-- addNyssaJsonFile(filePath, minimumLevel)
//     +-- addProvider(provider)
//     +-- addStream(stream, minimumLevel)
//     +-- createLogger(categoryName, bindings)
//     +-- dispatchPinoLine(rawLine)
//     +-- dispose()
//     +-- getEffectiveLevel(categoryName)
//     +-- isLevelEnabled(categoryName, level)
//     +-- new(minimumLevel, clock)
//     +-- pinoRoot
//     +-- setMinimumLevel(level)
//     +-- setPackageLevel(packageName, level)
//     +-- startSpan(name, attributes, operationOrCallback, callback)
//     +-- withConsole(minimumLevel)
//     +-- withOtel(minimumLevel, loggerProvider)
//     +-- withRotatingJsonLog(filePath, minimumLevel)
//     `-- withStdErr(minimumLevel)

import type { LoggerProvider as OtelLoggerProvider } from "@opentelemetry/api-logs";
import * as os from "node:os";
import * as path from "node:path";
import pino, { type Logger as PinoInstance } from "pino";
import type { Logger } from "../Logger.js";
import type { LoggerProvider } from "../LoggerProvider.js";
import {
  isLogLevelEnabled,
  parseLogLevel,
  toPinoLevelName,
  type LogLevel
} from "../LogLevel.js";
import type { Clock } from "../fileLogging/Clock.js";
import { SystemClock } from "../fileLogging/SystemClock.js";
import { extractNyssaRecord } from "../fileLogging/extractNyssaRecord.js";
import { LogFile } from "../fileLogging/LogFile.js";
import type { LogRecord } from "../fileLogging/LogRecord.js";
import { StderrDiagnostics } from "../fileLogging/StderrDiagnostics.js";
import { OtelLogBridge } from "../telemetry/OtelLogBridge.js";
import type { SpanScope } from "../telemetry/SpanScope.js";
import { startSpan as startGlobalSpan } from "../telemetry/startSpan.js";
import {
  enrichWithActiveSpan,
  normalizeErrorArgs,
  normalizeNormalArgs
} from "./enrichWithActiveSpan.js";

const LEVEL_TAGS: Readonly<Record<LogLevel, string>> = {
  Trace: "trce",
  Debug: "dbug",
  Information: "info",
  Warning: "warn",
  Error: "fail",
  Critical: "crit",
  None: "none"
};

function formatUtcDate(date: Date): string {
  const y = date.getUTCFullYear();
  const m = String(date.getUTCMonth() + 1).padStart(2, "0");
  const d = String(date.getUTCDate()).padStart(2, "0");
  return `${y}.${m}.${d}`;
}

/**
 * Fluent builder managing Pino logger hierarchy, rotating JSONL files, stderr/stdout streams,
 * per-package (`a.b.c`) level overrides, and OpenTelemetry log/span bridging.
 */
export class LoggingBuilder {
  private minimumLevel: LogLevel;
  private readonly clock: Clock;
  private readonly packageLevels = new Map<string, LogLevel>();
  private readonly fileSinks: Array<{ readonly logFile: LogFile; readonly minimumLevel: LogLevel | null }> = [];
  private readonly customStreams: Array<{ readonly write: (line: string) => void; readonly minimumLevel: LogLevel | null }> = [];
  private readonly providers: LoggerProvider[] = [];
  private otelBridge: OtelLogBridge | null = null;
  private otelBridgeMinLevel: LogLevel | null = null;
  private stderrMinLevel: LogLevel | null = null;
  private consoleMinLevel: LogLevel | null = null;
  private hasStderr = false;
  private hasConsole = false;
  private disposed = false;
  public readonly pinoRoot: PinoInstance;

  public constructor(minimumLevel?: LogLevel | string, clock?: Clock) {
    this.minimumLevel =
      minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : "Information";
    this.clock = clock ?? new SystemClock();
    this.pinoRoot = pino(
      {
        level: "trace",
        base: null,
        timestamp: () => `,"time":${this.clock.now().getTime()}`
      },
      { write: (line: string) => this.dispatchPinoLine(line) }
    );
  }

  /** Set the root minimum log level for categories without a specific package override. */
  public setMinimumLevel(level: LogLevel | string): this {
    this.minimumLevel = parseLogLevel(String(level));
    return this;
  }

  /** Configure a hierarchical package/category level override (e.g., `"a.b.c"` or `"@nyssa-ai/db-gateway"`). */
  public setPackageLevel(packageName: string, level: LogLevel | string): this {
    const trimmed = packageName.trim();
    if (trimmed.length > 0) {
      this.packageLevels.set(trimmed, parseLogLevel(String(level)));
    }
    return this;
  }

  /** Resolve the effective minimum LogLevel for a category using longest-prefix matching (`a.b.c` -> `a.b` -> `a`). */
  public getEffectiveLevel(categoryName: string): LogLevel {
    let bestMatch = "";
    let bestLevel: LogLevel = this.minimumLevel;
    for (const [prefix, level] of this.packageLevels.entries()) {
      const matches =
        categoryName === prefix ||
        categoryName.startsWith(`${prefix}.`) ||
        categoryName.startsWith(`${prefix}/`) ||
        categoryName.startsWith(`${prefix}:`);
      if (matches && prefix.length > bestMatch.length) {
        bestMatch = prefix;
        bestLevel = level;
      }
    }
    return bestLevel;
  }

  /** Check whether a log record at `level` is enabled for `categoryName`. */
  public isLevelEnabled(categoryName: string, level: LogLevel): boolean {
    return !this.disposed && isLogLevelEnabled(level, this.getEffectiveLevel(categoryName));
  }

  /** Enable human-readable diagnostic output on `process.stderr`. */
  public withStdErr(minimumLevel?: LogLevel | string): this {
    this.hasStderr = true;
    this.stderrMinLevel = minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : null;
    return this;
  }

  /** Enable human-readable diagnostic output on `process.stdout`. */
  public withConsole(minimumLevel?: LogLevel | string): this {
    this.hasConsole = true;
    this.consoleMinLevel = minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : null;
    return this;
  }

  /** Enable OpenTelemetry log bridging into the global or supplied `LoggerProvider`. */
  public withOtel(minimumLevel?: LogLevel | string, loggerProvider?: OtelLoggerProvider): this {
    const resolvedMin = minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : "Trace";
    this.otelBridgeMinLevel = minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : null;
    this.otelBridge = new OtelLogBridge(resolvedMin, loggerProvider);
    return this;
  }

  /** Add a dated rotating JSONL file (`{yyyy.mm.dd}-{filename}`) in the target directory. */
  public withRotatingJsonLog(filePath: string, minimumLevel?: LogLevel | string): this {
    const directory = path.dirname(filePath);
    const effectiveDir = !directory || directory === "." ? process.cwd() : directory;
    const date = formatUtcDate(this.clock.now());
    return this.addFileDestination(path.join(effectiveDir, `${date}-${path.basename(filePath)}`), minimumLevel);
  }

  /** Add an exact-path rotating JSONL file destination. */
  public addNyssaJsonFile(filePath: string, minimumLevel?: LogLevel | string): this {
    return this.addFileDestination(filePath, minimumLevel);
  }

  /** Add a custom writable line stream receiving raw Pino/Nyssa JSON lines. */
  public addStream(
    stream: { write(line: string): unknown } | ((line: string) => void),
    minimumLevel?: LogLevel | string
  ): this {
    const write = typeof stream === "function" ? stream : (line: string) => { stream.write(line); };
    this.customStreams.push({
      write,
      minimumLevel: minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : null
    });
    return this;
  }

  /** Register a custom LoggerProvider for backward compatibility. */
  public addProvider(provider: LoggerProvider): this {
    this.providers.push(provider);
    return this;
  }

  /** Start a correlated OpenTelemetry span. */
  public startSpan<T = SpanScope>(
    name: string,
    attributes?: Readonly<Record<string, unknown>>,
    operationOrCallback?: string | ((span: SpanScope) => T),
    callback?: (span: SpanScope) => T
  ): T {
    return startGlobalSpan(name, attributes, operationOrCallback, callback);
  }

  /** Create a category-scoped Logger backed by a Pino child logger. */
  public createLogger(categoryName: string, bindings?: Readonly<Record<string, unknown>>): Logger {
    const pinoChild = this.pinoRoot.child({ category: categoryName, ...(bindings ?? {}) });
    const legacyLoggers = this.providers.map(p => p.createLogger(categoryName));

    const emitNormal = (
      level: LogLevel,
      messageOrFields: string | Readonly<Record<string, unknown>>,
      fieldsOrMessage?: Readonly<Record<string, unknown>> | string,
      operation?: string
    ): void => {
      if (!this.isLevelEnabled(categoryName, level)) return;
      const { message, fields } = normalizeNormalArgs(messageOrFields, fieldsOrMessage);
      const enriched = enrichWithActiveSpan(fields, operation, null, this.clock.now().toISOString());
      const pinoMethod = toPinoLevelName(level) as "trace" | "debug" | "info" | "warn" | "error" | "fatal";
      pinoChild[pinoMethod](enriched, message);
      for (const legacy of legacyLoggers) {
        if (level === "Trace") legacy.trace(message, fields, operation);
        else if (level === "Debug") legacy.debug(message, fields, operation);
        else if (level === "Information") legacy.info(message, fields, operation);
        else if (level === "Warning") legacy.warn(message, fields, operation);
      }
    };

    const emitError = (
      level: "Error" | "Critical",
      messageOrFields: string | Readonly<Record<string, unknown>>,
      errorOrMessage?: unknown,
      fields?: Readonly<Record<string, unknown>>,
      operation?: string
    ): void => {
      if (!this.isLevelEnabled(categoryName, level)) return;
      const { message, error, resolvedFields } = normalizeErrorArgs(messageOrFields, errorOrMessage, fields);
      const enriched = enrichWithActiveSpan(resolvedFields, operation, error, this.clock.now().toISOString());
      pinoChild[level === "Critical" ? "fatal" : "error"](enriched, message);
      for (const legacy of legacyLoggers) {
        if (level === "Critical") legacy.critical(message, error, resolvedFields, operation);
        else legacy.error(message, error, resolvedFields, operation);
      }
    };

    return {
      trace: (m, f, op) => emitNormal("Trace", m, f, op),
      debug: (m, f, op) => emitNormal("Debug", m, f, op),
      info: (m, f, op) => emitNormal("Information", m, f, op),
      warn: (m, f, op) => emitNormal("Warning", m, f, op),
      error: (m, e, f, op) => emitError("Error", m, e, f, op),
      critical: (m, e, f, op) => emitError("Critical", m, e, f, op),
      fatal: (m, e, f, op) => emitError("Critical", m, e, f, op),
      child: childBindings => this.createLogger(categoryName, { ...(bindings ?? {}), ...childBindings }),
      isEnabled: level => this.isLevelEnabled(categoryName, parseLogLevel(String(level)))
    };
  }

  /** Internal callback invoked by Pino's destination stream for every serialized log line. */
  public dispatchPinoLine(rawLine: string): void {
    if (this.disposed) return;
    try {
      const parsed = JSON.parse(rawLine) as Record<string, unknown>;
      const record = extractNyssaRecord(parsed, this.clock.now().toISOString());
      const recordLevel = parseLogLevel(record.Level);
      if (!this.isLevelEnabled(record.Module, recordLevel)) return;

      const effectiveMin = this.getEffectiveLevel(record.Module);
      if (this.hasStderr && isLogLevelEnabled(recordLevel, this.stderrMinLevel ?? effectiveMin)) {
        this.writeFormattedStream(process.stderr, record, recordLevel);
      }
      if (this.hasConsole && isLogLevelEnabled(recordLevel, this.consoleMinLevel ?? effectiveMin)) {
        this.writeFormattedStream(process.stdout, record, recordLevel);
      }
      for (const fileEntry of this.fileSinks) {
        if (isLogLevelEnabled(recordLevel, fileEntry.minimumLevel ?? effectiveMin)) {
          fileEntry.logFile.writeRecord(record);
        }
      }
      for (const streamEntry of this.customStreams) {
        if (isLogLevelEnabled(recordLevel, streamEntry.minimumLevel ?? effectiveMin)) {
          try { streamEntry.write(rawLine); } catch { /* contained */ }
        }
      }
      if (this.otelBridge !== null && isLogLevelEnabled(recordLevel, this.otelBridgeMinLevel ?? effectiveMin)) {
        this.otelBridge.emit(record);
      }
    } catch {
      // Sink containment
    }
  }

  /** Dispose all owned file descriptors and custom providers. */
  public dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    for (const fileEntry of this.fileSinks) fileEntry.logFile.dispose();
    for (const provider of this.providers) provider.dispose();
    if (this.otelBridge !== null) this.otelBridge.dispose();
  }

  private addFileDestination(destination: string, minimumLevel?: LogLevel | string): this {
    const logFile = new LogFile(destination, undefined, this.clock);
    const initError = logFile.initialize();
    if (initError !== null) {
      StderrDiagnostics.error(destination, initError);
      logFile.dispose();
      return this;
    }
    this.fileSinks.push({
      logFile,
      minimumLevel: minimumLevel !== undefined ? parseLogLevel(String(minimumLevel)) : null
    });
    return this;
  }

  private writeFormattedStream(stream: NodeJS.WriteStream, record: LogRecord, level: LogLevel): void {
    try {
      const tag = LEVEL_TAGS[level] ?? "info";
      let output = `${tag}: ${record.Module}${os.EOL}      ${record.Message}${os.EOL}`;
      if (record.Exception !== null && record.Exception.length > 0) {
        output += `      ${record.Exception}${os.EOL}`;
      }
      stream.write(output);
    } catch {
      // Sink containment
    }
  }
}
