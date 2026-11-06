// Responsibility: Options for one-call Nyssa logging configuration backed by Pino and OpenTelemetry.
// Public interface:
// `-- NyssaLoggingOptions
//     +-- appName
//     +-- clock
//     +-- console
//     +-- enableOtel
//     +-- filePath
//     +-- logDir
//     +-- minimumLevel
//     +-- packageLevels
//     `-- stderr

import type { Clock } from "../fileLogging/Clock.js";
import type { LogLevel } from "../LogLevel.js";

/**
 * Configuration options for `configureNyssaLogging`.
 */
export interface NyssaLoggingOptions {
  /** Application name used in the default dated filename `{yyyy.mm.dd}-{appName}-log.json`. */
  readonly appName?: string;
  /** Directory for default rotating JSON log files (defaults to `"./logs"`). */
  readonly logDir?: string;
  /** Explicit JSON log file path override, or `null`/`false` to disable the default rotating file. */
  readonly filePath?: string | null | false;
  /** Enable human-readable output on `process.stderr` (defaults to `true`). */
  readonly stderr?: boolean;
  /** Enable human-readable output on `process.stdout` (defaults to `false`). */
  readonly console?: boolean;
  /** Root minimum log level (defaults to `"Information"` or `NYSSA_LOG_LEVEL`). */
  readonly minimumLevel?: LogLevel | string;
  /** Per-package / per-category minimum log levels (`{ "a.b.c": "Debug" }`). */
  readonly packageLevels?: Readonly<Record<string, LogLevel | string>>;
  /** Enable OpenTelemetry log bridging (`@opentelemetry/api-logs`). */
  readonly enableOtel?: boolean;
  /** Optional deterministic clock for timestamps and file rotation tests. */
  readonly clock?: Clock;
}
