// Responsibility: Structure of one operational boundary log record emitted by ConfigurationClient.
// Public interface:
// `-- LogRecord
//     +-- correlationId
//     +-- durationMs
//     +-- error
//     +-- event
//     +-- index(key)
//     +-- level
//     +-- operation
//     +-- service
//     +-- timestamp
//     `-- version

/**
 * LogRecord models one structured operational log entry emitted by ConfigurationClient.
 */
export interface LogRecord {
  readonly timestamp: string;
  readonly level: "debug" | "info" | "warn" | "error";
  readonly event: string;
  readonly service: string;
  readonly version: string;
  readonly operation: string;
  readonly correlationId: string;
  readonly durationMs?: number | undefined;
  readonly error?: string | undefined;
  readonly [key: string]: unknown;
}
