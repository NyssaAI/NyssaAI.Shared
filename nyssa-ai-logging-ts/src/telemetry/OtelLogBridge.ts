// Responsibility: Bridge Nyssa LogRecord events into the OpenTelemetry Logs API with trace correlation.
// Public interface:
// `-- OtelLogBridge
//     +-- dispose()
//     +-- emit(record)
//     `-- new(minimumLevel, loggerProvider)

import { context } from "@opentelemetry/api";
import { logs, SeverityNumber, type LoggerProvider as OtelLoggerProvider } from "@opentelemetry/api-logs";
import { isLogLevelEnabled, parseLogLevel, type LogLevel } from "../LogLevel.js";
import type { LogRecord } from "../fileLogging/LogRecord.js";

const OTEL_SEVERITY_MAP: Readonly<Record<LogLevel, SeverityNumber>> = {
  Trace: SeverityNumber.TRACE,
  Debug: SeverityNumber.DEBUG,
  Information: SeverityNumber.INFO,
  Warning: SeverityNumber.WARN,
  Error: SeverityNumber.ERROR,
  Critical: SeverityNumber.FATAL,
  None: SeverityNumber.UNSPECIFIED
};

function coerceOtelAttr(value: unknown): string | number | boolean {
  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
    return value;
  }
  return String(value);
}

/**
 * Bridges Nyssa structured log records into the OpenTelemetry `LoggerProvider`.
 */
export class OtelLogBridge {
  private readonly minimumLevel: LogLevel;
  private readonly customProvider: OtelLoggerProvider | undefined;

  public constructor(minimumLevel: LogLevel = "Information", loggerProvider?: OtelLoggerProvider) {
    this.minimumLevel = minimumLevel;
    this.customProvider = loggerProvider;
  }

  /** Forward a Nyssa LogRecord into the OpenTelemetry logs pipeline. */
  public emit(record: LogRecord): void {
    const level = parseLogLevel(record.Level);
    if (!isLogLevelEnabled(level, this.minimumLevel)) {
      return;
    }

    try {
      const provider = this.customProvider ?? logs.getLoggerProvider();
      const otelLogger = provider.getLogger(record.Module || "nyssa-ai-logging");

      const attributes: Record<string, string | number | boolean> = {
        "code.namespace": record.Module
      };
      if (record.Operation) {
        attributes["nyssa.operation"] = record.Operation;
      }
      if (record.Exception) {
        attributes["exception.message"] = record.Exception;
      }
      for (const [key, value] of Object.entries(record.AdditionalFields)) {
        if (value !== null && value !== undefined) {
          attributes[key] = coerceOtelAttr(value);
        }
      }

      otelLogger.emit({
        timestamp: Date.parse(record.Timestamp) || Date.now(),
        observedTimestamp: Date.now(),
        severityNumber: OTEL_SEVERITY_MAP[level] ?? SeverityNumber.INFO,
        severityText: record.Level,
        body: record.Message,
        attributes,
        context: context.active()
      });
    } catch {
      // Sink containment: OpenTelemetry failures never interrupt callers
    }
  }

  /** Dispose the bridge (no-op as OpenTelemetry SDK lifecycle is owned by the host). */
  public dispose(): void {
    // Host owns OTel SDK shutdown
  }
}
