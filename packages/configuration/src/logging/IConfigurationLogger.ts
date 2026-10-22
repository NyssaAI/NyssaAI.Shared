// Responsibility: Contract for operational boundary logging complying with TS-LOG.
// Public interface:
// +-- IConfigurationLogger
// |   `-- log(record)
// `-- LogRecord
//     +-- correlationId
//     +-- durationMs
//     +-- error
//     +-- event
//     +-- level
//     +-- operation
//     +-- service
//     +-- timestamp
//     `-- version

export type LogLevel = "debug" | "info" | "warn" | "error";

export interface LogRecord {
  readonly timestamp: string;
  readonly level: LogLevel;
  readonly event: string;
  readonly service: string;
  readonly version: string;
  readonly operation: string;
  readonly correlationId: string;
  readonly durationMs?: number;
  readonly error?: string;
  readonly [key: string]: unknown;
}

export interface IConfigurationLogger {
  /**
   * Emits a structured log record. Must never throw or interrupt domain flow.
   */
  log(record: LogRecord): void;
}
